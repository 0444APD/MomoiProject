using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GBible.CodeGen
{
    // [PlayerData] 클래스의 필드 쓰기를 기록 호출로 바꿔 넣는다(Unity의 ILPostProcessor·코어 테스트 도구가 함께 쓴다).
    //
    //   1. [PlayerData] 클래스를 정의한 어셈블리: 기록할 필드마다 숨은 설정 메서드를 만든다.
    //        public static void __gs_set_hp(Unit owner, int value)
    //        { PlayerDataTracker.Write<int>(owner, "hp", owner.hp, value); owner.hp = value; }
    //   2. 모든 어셈블리: 그 필드에 쓰는 명령(stfld)을 설정 메서드 호출로 바꾼다. 스택 모양이 같아 다른 명령은 그대로.
    //
    // 기록할 필드 = 저장소가 다루는 필드(public 또는 [SerializeField]·[SerializeReference], readonly·[NonSerialized]·자동 속성 숨은 필드 제외).
    // 제네릭 [PlayerData] 클래스는 지원하지 않는다(경고). 구조체 필드를 제자리에서 고치는 코드(u.pos.x = 3)는 기록되지 않는다(개발 빌드의 누락 검사가 알린다).
    public static class PlayerDataWeaver
    {
        public const string AttributeName = "GBible.PlayerDataAttribute";
        public const string WovenAttributeName = "GBible.PlayerDataWovenAttribute";
        public const string CoreAssembly = "GBible.Core";
        public const string SetterPrefix = "__gs_set_";

        // 바꾼 것이 있으면 true. warnings: 사람이 읽는 경고(기록되지 않는 코드 등).
        public static bool Weave(ModuleDefinition module, List<string> warnings)
        {
            if (module.Assembly.Name.Name == CoreAssembly) return false;
            var core = module.AssemblyReferences.FirstOrDefault(r => r.Name == CoreAssembly);
            if (core == null) return false;

            var changed = false;
            var setters = new Dictionary<FieldDefinition, MethodDefinition>();
            var warned = new HashSet<string>(StringComparer.Ordinal);

            // 1. 이 어셈블리의 [PlayerData] 클래스에 설정 메서드를 만든다.
            foreach (var type in module.GetTypes().ToList())
            {
                if (!type.IsClass || type.IsValueType || !IsPlayerDataClass(type)) continue;
                if (type.HasGenericParameters)
                {
                    Warn(warnings, warned, $"{type.FullName}: 제네릭 [PlayerData] 클래스는 필드 쓰기를 기록하지 않습니다.");
                    continue;
                }
                // 위빙했다는 표시(저장소가 보고 쓰는 순간 기록 방식을 고른다).
                if (!type.CustomAttributes.Any(a => a.AttributeType.FullName == WovenAttributeName))
                {
                    var marker = new TypeReference("GBible", "PlayerDataWovenAttribute", module, core);
                    var constructor = new MethodReference(".ctor", module.TypeSystem.Void, marker) { HasThis = true };
                    type.CustomAttributes.Add(new CustomAttribute(module.ImportReference(constructor)));
                    changed = true;
                }
                foreach (var field in type.Fields.Where(IsTrackedField).ToList())
                {
                    var name = SetterPrefix + field.Name;
                    var existing = type.Methods.FirstOrDefault(m => m.Name == name);
                    if (existing != null)
                    {
                        setters[field] = existing;
                        continue;
                    }
                    setters[field] = CreateSetter(module, core, type, field);
                    changed = true;
                }
            }

            // 2. 모든 메서드의 필드 쓰기를 설정 메서드 호출로 바꾼다.
            foreach (var type in module.GetTypes())
            foreach (var method in type.Methods)
            {
                if (!method.HasBody || method.Name.StartsWith(SetterPrefix, StringComparison.Ordinal)) continue;
                foreach (var instruction in method.Body.Instructions)
                {
                    if (!(instruction.Operand is FieldReference reference)) continue;
                    if (instruction.OpCode != OpCodes.Stfld) continue;
                    if (reference.DeclaringType is GenericInstanceType) continue;
                    var field = TryResolve(reference);
                    if (field == null || !IsTrackedField(field) || !IsPlayerDataClass(field.DeclaringType) || field.DeclaringType.HasGenericParameters) continue;

                    instruction.OpCode = OpCodes.Call;
                    instruction.Operand = setters.TryGetValue(field, out var local)
                        ? (MethodReference)local
                        : SetterReference(module, reference, field);
                    changed = true;
                }
            }
            return changed;
        }

        static void Warn(List<string> warnings, HashSet<string> warned, string message)
        {
            if (warned.Add(message)) warnings?.Add(message);
        }

        static FieldDefinition TryResolve(FieldReference reference)
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

        // 클래스 자신이나 부모에 [PlayerData]가 있는지.
        public static bool IsPlayerDataClass(TypeDefinition type)
        {
            for (var current = type; current != null;)
            {
                if (current.CustomAttributes.Any(a => a.AttributeType.FullName == AttributeName)) return true;
                if (current.BaseType == null) return false;
                try
                {
                    current = current.BaseType.Resolve();
                }
                catch (AssemblyResolutionException)
                {
                    return false;
                }
            }
            return false;
        }

        // 저장소가 다루는 필드인지(DataModel과 같은 규칙).
        public static bool IsTrackedField(FieldDefinition field)
        {
            if (field.IsStatic || field.IsInitOnly || field.IsLiteral || field.IsNotSerialized) return false;
            if (field.Name.Contains("<")) return false;
            if (field.IsPublic) return true;
            return field.CustomAttributes.Any(a => a.AttributeType.Name == "SerializeField" || a.AttributeType.Name == "SerializeReference");
        }

        static MethodDefinition CreateSetter(ModuleDefinition module, AssemblyNameReference core, TypeDefinition type, FieldDefinition field)
        {
            var setter = new MethodDefinition(SetterPrefix + field.Name,
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, module.TypeSystem.Void);
            setter.Parameters.Add(new ParameterDefinition("owner", ParameterAttributes.None, type));
            setter.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, field.FieldType));

            var write = WriteMethod(module, core, field.FieldType);
            var il = setter.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, field.Name);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, write);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, field);
            il.Emit(OpCodes.Ret);
            type.Methods.Add(setter);
            return setter;
        }

        // PlayerDataTracker.Write<T>(object owner, string field, T old, T value)
        static MethodReference WriteMethod(ModuleDefinition module, AssemblyNameReference core, TypeReference valueType)
        {
            var tracker = new TypeReference("GBible", "PlayerDataTracker", module, core);
            var write = new MethodReference("Write", module.TypeSystem.Void, tracker) { HasThis = false };
            var t = new GenericParameter("T", write);
            write.GenericParameters.Add(t);
            write.Parameters.Add(new ParameterDefinition(module.TypeSystem.Object));
            write.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
            write.Parameters.Add(new ParameterDefinition(t));
            write.Parameters.Add(new ParameterDefinition(t));
            var instance = new GenericInstanceMethod(write);
            instance.GenericArguments.Add(module.ImportReference(valueType));
            return module.ImportReference(instance);
        }

        // 다른 어셈블리의 [PlayerData] 클래스 필드에 쓰는 경우: 그 어셈블리가 위빙될 때 생기는 설정 메서드를 이름으로 가리킨다.
        static MethodReference SetterReference(ModuleDefinition module, FieldReference reference, FieldDefinition field)
        {
            var owner = module.ImportReference(field.DeclaringType);
            var setter = new MethodReference(SetterPrefix + field.Name, module.TypeSystem.Void, owner) { HasThis = false };
            setter.Parameters.Add(new ParameterDefinition(owner));
            setter.Parameters.Add(new ParameterDefinition(module.ImportReference(reference.FieldType)));
            return setter;
        }
    }
}
