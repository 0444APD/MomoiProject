using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Unity.CompilationPipeline.Common.Diagnostics;
using Unity.CompilationPipeline.Common.ILPostProcessing;

namespace GBible.CodeGen
{
    // Unity가 스크립트를 컴파일할 때마다 부르는 단계. GBible.Core와 그것을 참조하는 어셈블리만 처리한다.
    // 실제 생성은 MirrorWeaver(코어 테스트 도구와 공용): [Serializable] 형식마다 Mirror 비교 코드를 만든다.
    // 이 파일은 Unity의 ILPostProcessor 연결만 맡는다.
    // 참고: ILPostProcessor는 공식 문서가 없는 Unity API라 Unity 버전을 올리면 동작을 확인한다(Netcode·Entities가 같은 방식).
    sealed class MirrorILPostProcessor : ILPostProcessor
    {
        public override ILPostProcessor GetInstance() => this;

        public override bool WillProcess(ICompiledAssembly compiledAssembly)
        {
            if (compiledAssembly.Name.EndsWith(".CodeGen", StringComparison.Ordinal)) return false;
            if (compiledAssembly.InMemoryAssembly.PeData == null || compiledAssembly.InMemoryAssembly.PdbData == null) return false;
            return compiledAssembly.Name == MirrorWeaver.CoreAssembly ||
                   compiledAssembly.References.Any(r => Path.GetFileNameWithoutExtension(r) == MirrorWeaver.CoreAssembly);
        }

        public override ILPostProcessResult Process(ICompiledAssembly compiledAssembly)
        {
            var diagnostics = new List<DiagnosticMessage>();
            AssemblyDefinition assembly;
            try
            {
                assembly = Read(compiledAssembly);
            }
            catch (BadImageFormatException)
            {
                return new ILPostProcessResult(null, diagnostics);
            }

            var warnings = new List<string>();
            bool changed;
            try
            {
                changed = MirrorWeaver.Weave(assembly.MainModule, warnings);
            }
            catch (Exception e)
            {
                diagnostics.Add(new DiagnosticMessage
                {
                    DiagnosticType = DiagnosticType.Error,
                    MessageData = $"[MomoiProject] Mirror 비교 코드 생성 실패({compiledAssembly.Name}): {e}",
                });
                return new ILPostProcessResult(null, diagnostics);
            }
            // 테스트 어셈블리는 일부러 다루지 못하는 형식을 담기도 하므로(그 경우를 시험) 경고를 내지 않는다 — 콘솔에는 게임 코드의 경고만.
            var isTest = compiledAssembly.References.Any(r =>
            {
                var name = Path.GetFileNameWithoutExtension(r);
                return name == "nunit.framework" || name == "UnityEngine.TestRunner";
            });
            if (!isTest)
                foreach (var warning in warnings)
                    diagnostics.Add(new DiagnosticMessage { DiagnosticType = DiagnosticType.Warning, MessageData = "[MomoiProject] " + warning });
            if (!changed) return new ILPostProcessResult(null, diagnostics);

            // 자기 자신을 가리키는 참조가 생겼으면 뺀다(Cecil이 같은 어셈블리를 참조로 더하는 경우).
            var self = assembly.Name.FullName;
            var selfReference = assembly.MainModule.AssemblyReferences.FirstOrDefault(r => r.FullName == self);
            if (selfReference != null) assembly.MainModule.AssemblyReferences.Remove(selfReference);

            var pe = new MemoryStream();
            var pdb = new MemoryStream();
            assembly.Write(pe, new WriterParameters { SymbolWriterProvider = new PortablePdbWriterProvider(), SymbolStream = pdb, WriteSymbols = true });
            return new ILPostProcessResult(new InMemoryAssembly(pe.ToArray(), pdb.ToArray()), diagnostics);
        }

        static AssemblyDefinition Read(ICompiledAssembly compiledAssembly)
        {
            var resolver = new Resolver(compiledAssembly);
            var parameters = new ReaderParameters
            {
                SymbolStream = new MemoryStream(compiledAssembly.InMemoryAssembly.PdbData),
                SymbolReaderProvider = new PortablePdbReaderProvider(),
                AssemblyResolver = resolver,
                ReadingMode = ReadingMode.Immediate,
            };
            var assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(compiledAssembly.InMemoryAssembly.PeData), parameters);
            resolver.Self = assembly;
            return assembly;
        }

        // 컴파일 중인 어셈블리의 참조 목록에서 찾는다(간접 참조는 같은 폴더에서).
        sealed class Resolver : IAssemblyResolver
        {
            readonly Dictionary<string, string> _paths = new Dictionary<string, string>(StringComparer.Ordinal);
            readonly HashSet<string> _directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            readonly Dictionary<string, AssemblyDefinition> _cache = new Dictionary<string, AssemblyDefinition>(StringComparer.Ordinal);
            readonly string _name;
            public AssemblyDefinition Self;

            public Resolver(ICompiledAssembly compiledAssembly)
            {
                _name = compiledAssembly.Name;
                foreach (var reference in compiledAssembly.References)
                {
                    _paths[Path.GetFileNameWithoutExtension(reference)] = reference;
                    _directories.Add(Path.GetDirectoryName(reference));
                }
            }

            public AssemblyDefinition Resolve(AssemblyNameReference name) => Resolve(name, new ReaderParameters(ReadingMode.Deferred));

            public AssemblyDefinition Resolve(AssemblyNameReference name, ReaderParameters parameters)
            {
                if (name.Name == _name) return Self;
                if (!_paths.TryGetValue(name.Name, out var path))
                {
                    path = _directories.Select(d => Path.Combine(d, name.Name + ".dll")).FirstOrDefault(File.Exists);
                    if (path == null) return null;
                }
                if (_cache.TryGetValue(path, out var cached)) return cached;
                parameters.AssemblyResolver = this;
                var assembly = AssemblyDefinition.ReadAssembly(ReadAllBytes(path), parameters);
                _cache[path] = assembly;
                return assembly;
            }

            static MemoryStream ReadAllBytes(string path)
            {
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            var bytes = new byte[file.Length];
                            var read = 0;
                            while (read < bytes.Length)
                            {
                                var n = file.Read(bytes, read, bytes.Length - read);
                                if (n == 0) break;
                                read += n;
                            }
                            return new MemoryStream(bytes);
                        }
                    }
                    catch (IOException) when (attempt < 10)
                    {
                        Thread.Sleep(200);
                    }
                }
            }

            public void Dispose()
            {
            }
        }
    }
}
