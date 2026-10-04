using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GBible.CodeGen
{
    // Mirror 생성기: [Serializable] 클래스·구조체마다 Mirror가 쓰는 메서드를 그 형식 안에 만든다(Unity의 ILPostProcessor·코어 테스트 도구가 함께 쓴다).
    // 형식 안에 만들기 때문에 private 필드도 다룰 수 있고, 게임 코드는 아무것도 바꾸지 않는다(필드 쓰기를 고치지 않는다).
    //
    //   클래스 T:
    //     static void __gb_sync(T live, MirrorNode n, ChangeSet cs)    필드마다 사본과 비교(숫자는 여기서 비교, 다를 때만 MirrorCode 호출)
    //     static void __gb_write(MirrorWriter w, T v)                 필드를 바이트로
    //     static void __gb_read(T t, MirrorReader r)                  바이트를 필드로
    //     static T    __gb_new()                                      빈 객체(인자 없는 생성자, 없으면 생성자 없이)
    //     static void __gb_apply(T t, int field, int depth, MirrorReader r, MirrorOp op)  기록 한 줄을 필드 하나에
    //     static void __gb_register()                                 위 메서드를 Mirror에 등록(게임 시작 때 한 번)
    //   구조체 T: __gb_eq(T a, T b) · __gb_write · __gb_read(r) → T · __gb_register
    //   모두(추상 클래스 포함): __gb_get(object o, int i) → object · __gb_set(object o, int i, object v)
    //     이 형식에 선언된 읽을 수 있는 멤버(저장 필드 + readonly 공개 필드 + 공개 읽기 속성)를 번호로 읽고 쓴다(필드 경로·저장·이벤트 값 읽기).
    //     부모 형식의 멤버는 부모의 것으로(FieldAccess가 부모를 따라 올라간다). 쓰기는 readonly가 아닌 필드만.
    // 대상: [Serializable]·[PlayerData]·[GameEvent]·[Intent] 형식.
    //
    // 필드 = Unity가 저장하는 필드(public 또는 [SerializeField]·[SerializeReference], static·readonly·const·[NonSerialized] 제외).
    // 필드 번호는 부모 클래스의 필드가 먼저다(부모도 생성 대상이면 부모의 메서드를 먼저 부른다).
    // 제네릭 형식, UnityEngine.Object 파생, 예외·어트리뷰트 클래스는 만들지 않는다.
    public static class MirrorWeaver
    {
        public const string CoreAssembly = "GBible.Core";
        const string Prefix = "__gb_";

        // MirrorKind(코어)와 같은 값.
        const byte KNone = 0, KBool = 1, KSByte = 2, KByte = 3, KInt16 = 4, KUInt16 = 5, KInt32 = 6, KUInt32 = 7, KInt64 = 8, KUInt64 = 9,
            KSingle = 10, KDouble = 11, KChar = 12, KString = 20, KStruct = 30, KObject = 40, KList = 50, KArray = 51, KDict = 60, KSkip = 255;

        public static bool Weave(ModuleDefinition module, List<string> warnings)
        {
            var isCore = module.Assembly.Name.Name == CoreAssembly;
            var coreReference = module.AssemblyReferences.FirstOrDefault(r => r.Name == CoreAssembly);
            if (!isCore && coreReference == null) return false;
            ModuleDefinition coreModule;
            try
            {
                coreModule = isCore ? module : module.AssemblyResolver.Resolve(coreReference)?.MainModule;
            }
            catch (AssemblyResolutionException)
            {
                coreModule = null;
            }
            if (coreModule == null || coreModule.GetType("GBible.MirrorCode") == null)
            {
                warnings?.Add($"{module.Assembly.Name.Name}: {CoreAssembly}의 Mirror 코드를 찾지 못해 비교 코드를 만들지 않았습니다.");
                return false;
            }
            var context = new Context(module, coreModule, warnings);
            var targets = module.GetTypes().Where(t => t.Module == module && IsTarget(t) && !IsWoven(t)).ToList();
            foreach (var type in BaseFirst(targets)) context.Generate(type);
            return context.Changed;
        }

        static bool IsWoven(TypeDefinition type) => type.Methods.Any(m => m.Name == Prefix + "write");

        // 생성 대상 형식인지.
        public static bool IsTarget(TypeDefinition type)
        {
            if (type == null || type.IsInterface || type.IsEnum || type.HasGenericParameters || type.Name.Contains("<") || type.Name == "<Module>") return false;
            for (var outer = type.DeclaringType; outer != null; outer = outer.DeclaringType)
                if (outer.HasGenericParameters) return false;
            if (type.IsAbstract && type.IsSealed) return false; // static class
            if (!type.IsSerializable && !type.CustomAttributes.Any(a => a.AttributeType.FullName == "GBible.PlayerDataAttribute" || a.AttributeType.FullName == "GBible.GameEventAttribute" || a.AttributeType.FullName == "GBible.IntentAttribute")) return false;
            for (var current = type.BaseType; current != null;)
            {
                var name = current.FullName;
                if (name == "UnityEngine.Object" || name == "System.Exception" || name == "System.Attribute" || name == "System.MulticastDelegate" || name == "System.Delegate") return false;
                if (name == "System.Object" || name == "System.ValueType") break;
                var resolved = TryResolve(current);
                if (resolved == null) break;
                current = resolved.BaseType;
            }
            return true;
        }

        static IEnumerable<TypeDefinition> BaseFirst(List<TypeDefinition> types)
        {
            var set = new HashSet<TypeDefinition>(types);
            var done = new HashSet<TypeDefinition>();
            var order = new List<TypeDefinition>();
            void Visit(TypeDefinition t)
            {
                if (!done.Add(t)) return;
                var parent = t.BaseType == null ? null : TryResolve(t.BaseType);
                if (parent != null && set.Contains(parent)) Visit(parent);
                order.Add(t);
            }
            foreach (var t in types) Visit(t);
            return order;
        }

        static TypeDefinition TryResolve(TypeReference reference)
        {
            try
            {
                return reference.Resolve();
            }
            catch (AssemblyResolutionException)
            {
                return null;
            }
        }

        // Unity가 저장하는 필드인지.
        public static bool IsField(FieldDefinition field)
        {
            if (field.IsStatic || field.IsInitOnly || field.IsLiteral || field.IsNotSerialized) return false;
            var serialized = field.CustomAttributes.Any(a => a.AttributeType.Name is "SerializeField" or "SerializeReference" or "SerializeFieldAttribute" or "SerializeReferenceAttribute");
            if (field.Name.Contains("<")) return serialized; // [field: SerializeField] 자동 속성
            return field.IsPublic || serialized;
        }

        struct FieldPlan
        {
            public FieldDefinition Field;
            public byte Kind;
            public TypeReference A;
            public TypeReference B;
        }

        // 필드 형식을 Mirror 종류로.
        static FieldPlan Classify(FieldDefinition field)
        {
            var plan = new FieldPlan { Field = field, Kind = KindOf(field.FieldType, out var a, out var b) };
            plan.A = a;
            plan.B = b;
            return plan;
        }

        static byte KindOf(TypeReference type, out TypeReference a, out TypeReference b)
        {
            a = b = null;
            switch (type.MetadataType)
            {
                case MetadataType.Boolean: return KBool;
                case MetadataType.SByte: return KSByte;
                case MetadataType.Byte: return KByte;
                case MetadataType.Int16: return KInt16;
                case MetadataType.UInt16: return KUInt16;
                case MetadataType.Int32: return KInt32;
                case MetadataType.UInt32: return KUInt32;
                case MetadataType.Int64: return KInt64;
                case MetadataType.UInt64: return KUInt64;
                case MetadataType.Single: return KSingle;
                case MetadataType.Double: return KDouble;
                case MetadataType.Char: return KChar;
                case MetadataType.String: return KString;
                case MetadataType.Pointer:
                case MetadataType.ByReference:
                case MetadataType.FunctionPointer:
                case MetadataType.IntPtr:
                case MetadataType.UIntPtr:
                case MetadataType.Var:
                case MetadataType.MVar:
                    return KSkip;
            }
            if (type is ArrayType array)
            {
                if (array.Rank != 1 || IsCollection(array.ElementType)) return KNone;
                a = array.ElementType;
                return KArray;
            }
            if (type is GenericInstanceType generic)
            {
                var name = generic.ElementType.FullName;
                if (name == "System.Collections.Generic.List`1")
                {
                    a = generic.GenericArguments[0];
                    return IsCollection(a) ? KNone : KList;
                }
                if (name == "System.Collections.Generic.Dictionary`2")
                {
                    a = generic.GenericArguments[0];
                    b = generic.GenericArguments[1];
                    return !IsKey(a) || IsCollection(b) ? KNone : KDict;
                }
            }
            var definition = TryResolve(type);
            if (definition == null) return type.IsValueType ? KStruct : KObject;
            if (definition.IsEnum)
            {
                var value = definition.Fields.FirstOrDefault(f => f.Name == "value__");
                return value == null ? KInt32 : KindOf(value.FieldType, out _, out _);
            }
            for (var current = definition.BaseType; current != null;)
            {
                if (current.FullName == "System.MulticastDelegate") return KSkip;
                if (current.FullName == "System.Object" || current.FullName == "System.ValueType") break;
                var resolved = TryResolve(current);
                if (resolved == null) break;
                current = resolved.BaseType;
            }
            return definition.IsValueType ? KStruct : KObject;
        }

        static bool IsCollection(TypeReference type) =>
            type is ArrayType || (type is GenericInstanceType g && (g.ElementType.FullName == "System.Collections.Generic.List`1" || g.ElementType.FullName == "System.Collections.Generic.Dictionary`2"));

        static bool IsKey(TypeReference type)
        {
            switch (type.MetadataType)
            {
                case MetadataType.String:
                case MetadataType.Int32:
                case MetadataType.Int64:
                case MetadataType.Int16:
                case MetadataType.UInt16:
                case MetadataType.UInt32:
                case MetadataType.UInt64:
                case MetadataType.Byte:
                case MetadataType.SByte:
                case MetadataType.Char:
                case MetadataType.Boolean:
                    return true;
            }
            var definition = TryResolve(type);
            return definition != null && definition.IsEnum;
        }

        static bool IsBits(byte kind) => kind >= KBool && kind <= KChar;

        sealed class Generated
        {
            public MethodDefinition Sync, Write, Read, Apply;
        }

        sealed class Context
        {
            readonly ModuleDefinition _module;
            readonly ModuleDefinition _core;
            readonly List<string> _warnings;
            readonly TypeDefinition _code;
            readonly Dictionary<TypeDefinition, Generated> _generated = new Dictionary<TypeDefinition, Generated>();
            readonly Dictionary<TypeDefinition, List<FieldPlan>> _layouts = new Dictionary<TypeDefinition, List<FieldPlan>>();
            public bool Changed;

            readonly TypeReference _node, _changes, _writer, _reader, _op;
            readonly FieldReference _nodeL;

            public Context(ModuleDefinition module, ModuleDefinition core, List<string> warnings)
            {
                _module = module;
                _core = core;
                _warnings = warnings;
                _code = core.GetType("GBible.MirrorCode");
                _node = Import("GBible.MirrorNode");
                _changes = Import("GBible.ChangeSet");
                _writer = Import("GBible.MirrorWriter");
                _reader = Import("GBible.MirrorReader");
                _op = Import("GBible.MirrorOp");
                _nodeL = module.ImportReference(core.GetType("GBible.MirrorNode").Fields.First(f => f.Name == "L"));
            }

            TypeReference Import(string fullName) => _module.ImportReference(_core.GetType(fullName));

            MethodReference Code(string name) => _module.ImportReference(_code.Methods.Single(m => m.Name == name));

            MethodReference Code(string name, params TypeReference[] arguments)
            {
                var method = new GenericInstanceMethod(Code(name));
                foreach (var argument in arguments) method.GenericArguments.Add(_module.ImportReference(argument));
                return method;
            }

            void Warn(string message)
            {
                if (_warnings != null && !_warnings.Contains(message)) _warnings.Add(message);
            }

            // 부모 클래스(생성 대상이고 실제로 생성될 어셈블리에 있을 때).
            TypeDefinition BaseOf(TypeDefinition type)
            {
                if (type.IsValueType || type.BaseType == null || type.BaseType.FullName == "System.Object") return null;
                var parent = TryResolve(type.BaseType);
                if (parent == null || !IsTarget(parent)) return null;
                var module = parent.Module;
                var processed = module == _module || module.Assembly.Name.Name == CoreAssembly || module.AssemblyReferences.Any(r => r.Name == CoreAssembly);
                return processed ? parent : null;
            }

            // 이 형식의 필드 배치(부모 먼저).
            List<FieldPlan> Layout(TypeDefinition type)
            {
                if (_layouts.TryGetValue(type, out var cached)) return cached;
                var list = new List<FieldPlan>();
                var parent = BaseOf(type);
                if (parent != null) list.AddRange(Layout(parent));
                list.AddRange(Own(type));
                _layouts[type] = list;
                return list;
            }

            static IEnumerable<FieldPlan> Own(TypeDefinition type) => type.Fields.Where(IsField).Select(Classify).Where(p => p.Kind != KSkip);

            public void Generate(TypeDefinition type)
            {
                var own = Own(type).ToList();
                var all = Layout(type);
                var rawParent = type.IsValueType || type.BaseType == null ? null : TryResolve(type.BaseType);
                if (rawParent != null && rawParent.FullName != "System.Object" && BaseOf(type) == null && !IsTarget(rawParent))
                {
                    var lost = rawParent.Fields.Where(IsField).Select(f => f.Name).ToList();
                    if (lost.Count > 0)
                        Warn($"{type.FullName}: 부모 {rawParent.FullName}에 [Serializable]이 없어 그 필드({string.Join(", ", lost)})는 기록·비교하지 않습니다(Unity도 저장하지 않음). 부모에 [Serializable]을 붙이세요.");
                }
                foreach (var plan in own.Where(p => p.Kind == KNone))
                    Warn($"{type.FullName}.{plan.Field.Name}: 중첩 컬렉션·이 형식은 비교 코드로 다루지 않습니다(이 형식을 담은 플레이어 데이터는 예전 비교 방식).");
                if (type.IsValueType) GenerateStruct(type, own);
                else GenerateClass(type, own, all);
                Changed = true;
            }

            MethodDefinition NewMethod(TypeDefinition owner, string name, TypeReference returns)
            {
                var method = new MethodDefinition(Prefix + name, MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, returns);
                owner.Methods.Add(method);
                return method;
            }

            static ParameterDefinition Param(MethodDefinition method, string name, TypeReference type)
            {
                var parameter = new ParameterDefinition(name, ParameterAttributes.None, type);
                method.Parameters.Add(parameter);
                return parameter;
            }

            // ── 클래스 ──

            void GenerateClass(TypeDefinition type, List<FieldPlan> own, List<FieldPlan> all)
            {
                var parent = BaseOf(type);
                var baseCount = all.Count - own.Count;
                var generated = new Generated();

                // sync
                var sync = generated.Sync = NewMethod(type, "sync", _module.TypeSystem.Void);
                Param(sync, "live", type);
                Param(sync, "n", _node);
                Param(sync, "cs", _changes);
                sync.Body.InitLocals = true;
                var bits = new VariableDefinition(_module.TypeSystem.Int64);
                sync.Body.Variables.Add(bits);
                var il = sync.Body.GetILProcessor();
                if (parent != null)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Ldarg_2);
                    il.Emit(OpCodes.Call, BaseMethod(parent, g => g.Sync, "sync", _module.TypeSystem.Void, ImportType(parent), _node, _changes));
                }
                for (var j = 0; j < own.Count; j++) EmitSync(il, own[j], baseCount + j, bits);
                il.Emit(OpCodes.Ret);

                // write
                var write = generated.Write = NewMethod(type, "write", _module.TypeSystem.Void);
                Param(write, "w", _writer);
                Param(write, "v", type);
                il = write.Body.GetILProcessor();
                if (parent != null)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Call, BaseMethod(parent, g => g.Write, "write", _module.TypeSystem.Void, _writer, ImportType(parent)));
                }
                foreach (var plan in own) EmitWrite(il, plan, OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ret);

                // read
                var read = generated.Read = NewMethod(type, "read", _module.TypeSystem.Void);
                Param(read, "t", type);
                Param(read, "r", _reader);
                il = read.Body.GetILProcessor();
                if (parent != null)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Call, BaseMethod(parent, g => g.Read, "read", _module.TypeSystem.Void, ImportType(parent), _reader));
                }
                foreach (var plan in own)
                {
                    if (plan.Kind == KNone) continue;
                    il.Emit(OpCodes.Ldarg_0);
                    EmitRead(il, plan, OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Stfld, plan.Field);
                }
                il.Emit(OpCodes.Ret);

                // apply
                var apply = generated.Apply = NewMethod(type, "apply", _module.TypeSystem.Void);
                Param(apply, "t", type);
                Param(apply, "field", _module.TypeSystem.Int32);
                Param(apply, "depth", _module.TypeSystem.Int32);
                Param(apply, "r", _reader);
                Param(apply, "op", _op);
                il = apply.Body.GetILProcessor();
                if (parent != null)
                {
                    var mine = Instruction.Create(OpCodes.Nop);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Ldc_I4, baseCount);
                    il.Emit(OpCodes.Bge, mine);
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Ldarg_2);
                    il.Emit(OpCodes.Ldarg_3);
                    il.Emit(OpCodes.Ldarg_S, apply.Parameters[4]);
                    il.Emit(OpCodes.Call, BaseMethod(parent, g => g.Apply, "apply", _module.TypeSystem.Void, ImportType(parent), _module.TypeSystem.Int32, _module.TypeSystem.Int32, _reader, _op));
                    il.Emit(OpCodes.Ret);
                    il.Append(mine);
                }
                if (own.Count > 0)
                {
                    var labels = own.Select(_ => Instruction.Create(OpCodes.Nop)).ToArray();
                    il.Emit(OpCodes.Ldarg_1);
                    if (baseCount > 0)
                    {
                        il.Emit(OpCodes.Ldc_I4, baseCount);
                        il.Emit(OpCodes.Sub);
                    }
                    il.Emit(OpCodes.Switch, labels);
                    il.Emit(OpCodes.Call, Code("BadField"));
                    il.Emit(OpCodes.Ret);
                    for (var j = 0; j < own.Count; j++)
                    {
                        il.Append(labels[j]);
                        EmitApply(il, own[j], apply);
                        il.Emit(OpCodes.Ret);
                    }
                }
                else
                {
                    il.Emit(OpCodes.Call, Code("BadField"));
                    il.Emit(OpCodes.Ret);
                }
                _generated[type] = generated;

                var access = GenerateAccess(type);
                if (type.IsAbstract)
                {
                    var registerAccess = NewMethod(type, "register", _module.TypeSystem.Void);
                    il = registerAccess.Body.GetILProcessor();
                    EmitAccessRegister(il, type, access);
                    il.Emit(OpCodes.Ret);
                    return;
                }

                // new
                var create = NewMethod(type, "new", type);
                il = create.Body.GetILProcessor();
                var constructor = type.Methods.FirstOrDefault(m => m.IsConstructor && !m.IsStatic && m.Parameters.Count == 0);
                if (constructor != null) il.Emit(OpCodes.Newobj, constructor);
                else
                {
                    il.Emit(OpCodes.Ldtoken, type);
                    il.Emit(OpCodes.Call, Code("Uninitialized"));
                    il.Emit(OpCodes.Castclass, type);
                }
                il.Emit(OpCodes.Ret);

                // register
                var register = NewMethod(type, "register", _module.TypeSystem.Void);
                il = register.Body.GetILProcessor();
                EmitNames(il, all);
                EmitKinds(il, all);
                EmitDelegate(il, sync, "MirrorSyncFn`1", type);
                EmitDelegate(il, write, "MirrorWriteFn`1", type);
                EmitDelegate(il, read, "MirrorReadFn`1", type);
                EmitDelegate(il, create, "MirrorNewFn`1", type);
                EmitDelegate(il, apply, "MirrorApplyFn`1", type);
                il.Emit(OpCodes.Call, Code("RegisterClass", type));
                EmitNullables(il, all);
                EmitAccessRegister(il, type, access);
                il.Emit(OpCodes.Ret);
            }

            TypeReference ImportType(TypeDefinition type) => type.Module == _module ? type : _module.ImportReference(type);

            // 부모의 생성 메서드: 같은 어셈블리면 방금 만든 정의, 다른 어셈블리면 이름으로(그 어셈블리를 위빙할 때 생긴다).
            MethodReference BaseMethod(TypeDefinition parent, Func<Generated, MethodDefinition> pick, string name, TypeReference returns, params TypeReference[] parameters)
            {
                if (parent.Module == _module && _generated.TryGetValue(parent, out var local)) return pick(local);
                var existing = parent.Methods.FirstOrDefault(m => m.Name == Prefix + name);
                if (existing != null) return _module.ImportReference(existing);
                var reference = new MethodReference(Prefix + name, returns, ImportType(parent)) { HasThis = false };
                foreach (var p in parameters) reference.Parameters.Add(new ParameterDefinition(p));
                return _module.ImportReference(reference);
            }

            void EmitNames(ILProcessor il, List<FieldPlan> all)
            {
                il.Emit(OpCodes.Ldc_I4, all.Count);
                il.Emit(OpCodes.Newarr, _module.TypeSystem.String);
                for (var i = 0; i < all.Count; i++)
                {
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Ldc_I4, i);
                    il.Emit(OpCodes.Ldstr, all[i].Field.Name);
                    il.Emit(OpCodes.Stelem_Ref);
                }
            }

            void EmitKinds(ILProcessor il, List<FieldPlan> all)
            {
                il.Emit(OpCodes.Ldc_I4, all.Count);
                il.Emit(OpCodes.Newarr, _module.TypeSystem.Byte);
                for (var i = 0; i < all.Count; i++)
                {
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Ldc_I4, i);
                    il.Emit(OpCodes.Ldc_I4, (int)all[i].Kind);
                    il.Emit(OpCodes.Stelem_I1);
                }
            }

            void EmitDelegate(ILProcessor il, MethodDefinition target, string delegateName, TypeReference argument)
            {
                var definition = _core.GetType("GBible." + delegateName);
                var generic = new GenericInstanceType(_module.ImportReference(definition));
                generic.GenericArguments.Add(argument);
                var constructor = new MethodReference(".ctor", _module.TypeSystem.Void, generic) { HasThis = true };
                constructor.Parameters.Add(new ParameterDefinition(_module.TypeSystem.Object));
                constructor.Parameters.Add(new ParameterDefinition(_module.TypeSystem.IntPtr));
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldftn, target);
                il.Emit(OpCodes.Newobj, _module.ImportReference(constructor));
            }

            // ── 멤버 읽기·쓰기(__gb_get·__gb_set) ──

            sealed class Member
            {
                public string Name;
                public FieldDefinition Field;
                public PropertyDefinition Property;
                public TypeReference Type;
                public bool Settable;
            }

            sealed class Access
            {
                public List<Member> Members;
                public MethodDefinition Get, Set;
            }

            static bool Readable(TypeReference type)
            {
                switch (type.MetadataType)
                {
                    case MetadataType.Pointer:
                    case MetadataType.ByReference:
                    case MetadataType.FunctionPointer:
                    case MetadataType.TypedByReference:
                    case MetadataType.Var:
                    case MetadataType.MVar:
                    case MetadataType.RequiredModifier:
                    case MetadataType.OptionalModifier:
                    case MetadataType.Pinned:
                        return false;
                }
                return true;
            }

            static bool IsValueType(TypeReference type)
            {
                if (type is GenericInstanceType g) return TryResolve(g.ElementType)?.IsValueType ?? type.IsValueType;
                return TryResolve(type)?.IsValueType ?? type.IsValueType;
            }

            // 이 형식에 선언된 읽을 수 있는 멤버: 필드(공개 또는 [SerializeField]·[SerializeReference], readonly 포함, static·const 제외) + 공개 읽기 속성.
            static List<Member> MembersOf(TypeDefinition type)
            {
                var members = new List<Member>();
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var field in type.Fields)
                {
                    if (field.IsStatic || field.IsLiteral || !Readable(field.FieldType)) continue;
                    var serialized = field.CustomAttributes.Any(a => a.AttributeType.Name is "SerializeField" or "SerializeReference" or "SerializeFieldAttribute" or "SerializeReferenceAttribute");
                    if (!field.IsPublic && !serialized) continue;
                    if (!names.Add(field.Name)) continue;
                    members.Add(new Member { Name = field.Name, Field = field, Type = field.FieldType, Settable = !field.IsInitOnly });
                }
                foreach (var property in type.Properties)
                {
                    var getter = property.GetMethod;
                    if (getter == null || getter.IsStatic || !getter.IsPublic || getter.Parameters.Count > 0 || property.HasParameters) continue;
                    if (!Readable(property.PropertyType) || getter.HasGenericParameters || !names.Add(property.Name)) continue;
                    members.Add(new Member { Name = property.Name, Property = property, Type = property.PropertyType });
                }
                return members;
            }

            Access GenerateAccess(TypeDefinition type)
            {
                var access = new Access { Members = MembersOf(type) };
                var members = access.Members;
                var self = ImportType(type);

                // get
                var get = access.Get = NewMethod(type, "get", _module.TypeSystem.Object);
                Param(get, "o", _module.TypeSystem.Object);
                Param(get, "i", _module.TypeSystem.Int32);
                var il = get.Body.GetILProcessor();
                if (members.Count > 0)
                {
                    var labels = members.Select(_ => Instruction.Create(OpCodes.Nop)).ToArray();
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Switch, labels);
                    il.Emit(OpCodes.Ldnull);
                    il.Emit(OpCodes.Ret);
                    for (var i = 0; i < members.Count; i++)
                    {
                        var member = members[i];
                        il.Append(labels[i]);
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(type.IsValueType ? OpCodes.Unbox : OpCodes.Castclass, self);
                        if (member.Field != null) il.Emit(OpCodes.Ldfld, member.Field);
                        else il.Emit(type.IsValueType || !member.Property.GetMethod.IsVirtual ? OpCodes.Call : OpCodes.Callvirt, member.Property.GetMethod);
                        if (IsValueType(member.Type)) il.Emit(OpCodes.Box, member.Type);
                        il.Emit(OpCodes.Ret);
                    }
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                    il.Emit(OpCodes.Ret);
                }

                // set
                var set = access.Set = NewMethod(type, "set", _module.TypeSystem.Void);
                Param(set, "o", _module.TypeSystem.Object);
                Param(set, "i", _module.TypeSystem.Int32);
                Param(set, "v", _module.TypeSystem.Object);
                il = set.Body.GetILProcessor();
                var done = Instruction.Create(OpCodes.Ret);
                if (members.Count > 0)
                {
                    var labels = members.Select(m => m.Settable ? Instruction.Create(OpCodes.Nop) : done).ToArray();
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Switch, labels);
                    il.Emit(OpCodes.Br, done);
                    for (var i = 0; i < members.Count; i++)
                    {
                        var member = members[i];
                        if (!member.Settable) continue;
                        il.Append(labels[i]);
                        il.Emit(OpCodes.Ldarg_0);
                        il.Emit(type.IsValueType ? OpCodes.Unbox : OpCodes.Castclass, self);
                        il.Emit(OpCodes.Ldarg_2);
                        if (IsValueType(member.Type)) il.Emit(OpCodes.Unbox_Any, member.Type);
                        else if (member.Type.MetadataType != MetadataType.Object) il.Emit(OpCodes.Castclass, member.Type);
                        il.Emit(OpCodes.Stfld, member.Field);
                        il.Emit(OpCodes.Ret);
                    }
                }
                il.Append(done);
                return access;
            }

            // int? 같은 Nullable<T> 필드·요소가 있으면 그 값 연산을 등록한다(MirrorCode.RegisterNullable<T>).
            void EmitNullables(ILProcessor il, List<FieldPlan> plans)
            {
                var seen = new HashSet<string>();
                foreach (var plan in plans)
                    foreach (var type in new[] { plan.Field.FieldType, plan.A, plan.B })
                        if (type is GenericInstanceType g && g.ElementType.FullName == "System.Nullable`1" && seen.Add(g.GenericArguments[0].FullName))
                            il.Emit(OpCodes.Call, Code("RegisterNullable", g.GenericArguments[0]));
            }

            // FieldAccess에 이 형식의 멤버 이름·종류·읽기·쓰기를 등록한다.
            void EmitAccessRegister(ILProcessor il, TypeDefinition type, Access access)
            {
                var members = access.Members;
                il.Emit(OpCodes.Ldtoken, ImportType(type));
                il.Emit(OpCodes.Ldc_I4, members.Count);
                il.Emit(OpCodes.Newarr, _module.TypeSystem.String);
                for (var i = 0; i < members.Count; i++)
                {
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Ldc_I4, i);
                    il.Emit(OpCodes.Ldstr, members[i].Name);
                    il.Emit(OpCodes.Stelem_Ref);
                }
                // 0 = 저장 필드, 1 = readonly 필드, 2 = 속성
                il.Emit(OpCodes.Ldc_I4, members.Count);
                il.Emit(OpCodes.Newarr, _module.TypeSystem.Byte);
                for (var i = 0; i < members.Count; i++)
                {
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Ldc_I4, i);
                    il.Emit(OpCodes.Ldc_I4, members[i].Property != null ? 2 : members[i].Settable ? 0 : 1);
                    il.Emit(OpCodes.Stelem_I1);
                }
                EmitPlainDelegate(il, access.Get, "MirrorGetFn");
                EmitPlainDelegate(il, access.Set, "MirrorSetFn");
                il.Emit(OpCodes.Call, Code("RegisterAccess"));
            }

            void EmitPlainDelegate(ILProcessor il, MethodDefinition target, string delegateName)
            {
                var definition = _module.ImportReference(_core.GetType("GBible." + delegateName));
                var constructor = new MethodReference(".ctor", _module.TypeSystem.Void, definition) { HasThis = true };
                constructor.Parameters.Add(new ParameterDefinition(_module.TypeSystem.Object));
                constructor.Parameters.Add(new ParameterDefinition(_module.TypeSystem.IntPtr));
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ldftn, target);
                il.Emit(OpCodes.Newobj, _module.ImportReference(constructor));
            }

            // ── 필드 하나 ──

            void ToBits(ILProcessor il, byte kind)
            {
                switch (kind)
                {
                    case KBool:
                    case KByte:
                    case KUInt16:
                    case KChar:
                    case KUInt32:
                        il.Emit(OpCodes.Conv_U8);
                        break;
                    case KSByte:
                    case KInt16:
                    case KInt32:
                        il.Emit(OpCodes.Conv_I8);
                        break;
                    case KSingle:
                        il.Emit(OpCodes.Call, Code("F2B"));
                        break;
                    case KDouble:
                        il.Emit(OpCodes.Call, Code("D2B"));
                        break;
                }
            }

            void FromBits(ILProcessor il, byte kind)
            {
                switch (kind)
                {
                    case KBool:
                    case KByte:
                        il.Emit(OpCodes.Conv_U1);
                        break;
                    case KSByte:
                        il.Emit(OpCodes.Conv_I1);
                        break;
                    case KInt16:
                        il.Emit(OpCodes.Conv_I2);
                        break;
                    case KUInt16:
                    case KChar:
                        il.Emit(OpCodes.Conv_U2);
                        break;
                    case KInt32:
                        il.Emit(OpCodes.Conv_I4);
                        break;
                    case KUInt32:
                        il.Emit(OpCodes.Conv_U4);
                        break;
                    case KSingle:
                        il.Emit(OpCodes.Call, Code("B2F"));
                        break;
                    case KDouble:
                        il.Emit(OpCodes.Call, Code("B2D"));
                        break;
                }
            }

            // 필드 종류별 도우미(이름 접두 + 제네릭 인자).
            MethodReference Helper(string prefix, FieldPlan plan)
            {
                switch (plan.Kind)
                {
                    case KString: return Code(prefix + "Str");
                    case KStruct: return Code(prefix + "Struct", plan.Field.FieldType);
                    case KObject: return Code(prefix + "Obj", plan.Field.FieldType);
                    case KList: return Code(prefix + "List", plan.A);
                    case KArray: return Code(prefix + "Array", plan.A);
                    case KDict: return Code(prefix + "Dict", plan.A, plan.B);
                }
                throw new InvalidOperationException("helper for kind " + plan.Kind);
            }

            void EmitSync(ILProcessor il, FieldPlan plan, int index, VariableDefinition bits)
            {
                if (plan.Kind == KNone) return;
                if (IsBits(plan.Kind))
                {
                    var skip = Instruction.Create(OpCodes.Nop);
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, plan.Field);
                    ToBits(il, plan.Kind);
                    il.Emit(OpCodes.Stloc, bits);
                    il.Emit(OpCodes.Ldloc, bits);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Ldfld, _nodeL);
                    il.Emit(OpCodes.Ldc_I4, index);
                    il.Emit(OpCodes.Ldelem_I8);
                    il.Emit(OpCodes.Beq, skip);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Ldc_I4, index);
                    il.Emit(OpCodes.Ldloc, bits);
                    il.Emit(OpCodes.Ldarg_2);
                    il.Emit(OpCodes.Call, Code("SyncBits"));
                    il.Append(skip);
                    return;
                }
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, plan.Field);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4, index);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Call, Helper("Sync", plan));
            }

            // 스택: (없음) → 필드를 w에 쓴다. loadValue = 값(클래스 참조 또는 구조체 값)을 싣는 명령.
            void EmitWrite(ILProcessor il, FieldPlan plan, OpCode loadValue)
            {
                if (plan.Kind == KNone) return;
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(loadValue);
                il.Emit(OpCodes.Ldfld, plan.Field);
                if (IsBits(plan.Kind))
                {
                    ToBits(il, plan.Kind);
                    il.Emit(OpCodes.Ldc_I4, (int)plan.Kind);
                    il.Emit(OpCodes.Call, Code("WriteBits"));
                    return;
                }
                il.Emit(OpCodes.Call, Helper("Write", plan));
            }

            // 스택: (없음) → 필드 형식의 값. loadReader = 읽기 도구를 싣는 명령.
            void EmitRead(ILProcessor il, FieldPlan plan, OpCode loadReader)
            {
                il.Emit(loadReader);
                if (IsBits(plan.Kind))
                {
                    il.Emit(OpCodes.Ldc_I4, (int)plan.Kind);
                    il.Emit(OpCodes.Call, Code("ReadBits"));
                    FromBits(il, plan.Kind);
                    return;
                }
                il.Emit(OpCodes.Call, Helper("Read", plan));
            }

            void EmitApply(ILProcessor il, FieldPlan plan, MethodDefinition apply)
            {
                if (plan.Kind == KNone)
                {
                    il.Emit(OpCodes.Call, Code("BadField"));
                    return;
                }
                il.Emit(OpCodes.Ldarg_0);
                if (IsBits(plan.Kind))
                {
                    il.Emit(OpCodes.Ldarg_3);
                    il.Emit(OpCodes.Ldc_I4, (int)plan.Kind);
                    il.Emit(OpCodes.Call, Code("ReadBits"));
                    FromBits(il, plan.Kind);
                    il.Emit(OpCodes.Stfld, plan.Field);
                    return;
                }
                il.Emit(OpCodes.Ldflda, plan.Field);
                il.Emit(OpCodes.Ldarg_3);
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Ldarg_S, apply.Parameters[4]);
                il.Emit(OpCodes.Call, Helper("Apply", plan));
            }

            // ── 구조체 ──

            void GenerateStruct(TypeDefinition type, List<FieldPlan> own)
            {
                var access = GenerateAccess(type);
                // eq
                var eq = NewMethod(type, "eq", _module.TypeSystem.Boolean);
                Param(eq, "a", type);
                Param(eq, "b", type);
                var il = eq.Body.GetILProcessor();
                var no = Instruction.Create(OpCodes.Ldc_I4_0);
                foreach (var plan in own)
                {
                    if (plan.Kind == KNone) continue;
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, plan.Field);
                    if (IsBits(plan.Kind)) ToBits(il, plan.Kind);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Ldfld, plan.Field);
                    if (IsBits(plan.Kind))
                    {
                        ToBits(il, plan.Kind);
                        il.Emit(OpCodes.Bne_Un, no);
                        continue;
                    }
                    switch (plan.Kind)
                    {
                        case KString:
                            il.Emit(OpCodes.Call, Code("EqStr"));
                            break;
                        case KStruct:
                            il.Emit(OpCodes.Call, Code("EqStruct", plan.Field.FieldType));
                            break;
                        default:
                            il.Emit(OpCodes.Call, Code("EqRef"));
                            break;
                    }
                    il.Emit(OpCodes.Brfalse, no);
                }
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Ret);
                il.Append(no);
                il.Emit(OpCodes.Ret);

                // write
                var write = NewMethod(type, "write", _module.TypeSystem.Void);
                Param(write, "w", _writer);
                Param(write, "v", type);
                il = write.Body.GetILProcessor();
                foreach (var plan in own) EmitWrite(il, plan, OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ret);

                // read
                var read = NewMethod(type, "read", type);
                Param(read, "r", _reader);
                read.Body.InitLocals = true;
                var value = new VariableDefinition(type);
                read.Body.Variables.Add(value);
                il = read.Body.GetILProcessor();
                il.Emit(OpCodes.Ldloca, value);
                il.Emit(OpCodes.Initobj, type);
                foreach (var plan in own)
                {
                    if (plan.Kind == KNone) continue;
                    il.Emit(OpCodes.Ldloca, value);
                    EmitRead(il, plan, OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Stfld, plan.Field);
                }
                il.Emit(OpCodes.Ldloc, value);
                il.Emit(OpCodes.Ret);

                // register
                var register = NewMethod(type, "register", _module.TypeSystem.Void);
                il = register.Body.GetILProcessor();
                EmitDelegate(il, eq, "MirrorEqualFn`1", type);
                EmitDelegate(il, write, "MirrorWriteFn`1", type);
                EmitDelegate(il, read, "MirrorValueReadFn`1", type);
                il.Emit(OpCodes.Call, Code("RegisterStruct", type));
                EmitNullables(il, own);
                EmitAccessRegister(il, type, access);
                il.Emit(OpCodes.Ret);
            }
        }
    }
}
