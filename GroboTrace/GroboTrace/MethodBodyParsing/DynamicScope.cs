using System;
using System.Reflection;
using System.Reflection.Emit;

namespace GroboTrace.MethodBodyParsing
{
    internal class DynamicScope
    {
        static DynamicScope()
        {
            var assembly = typeof(DynamicMethod).Assembly;
            t_DynamicScope = assembly.GetType("System.Reflection.Emit.DynamicScope");
            t_VarArgsMethod = assembly.GetType("System.Reflection.Emit.VarArgMethod");
            t_RuntimeMethodInfo = assembly.GetType("System.Reflection.RuntimeMethodInfo");

            itemGetter = BuildItemGetter();
            getTokenFor = BuildGetTokenFor();
        }

        public DynamicScope(object inst)
        {
            this.inst = inst;
        }

        private static Func<object, int, object> BuildItemGetter()
        {
            var method = new DynamicMethod(Guid.NewGuid().ToString(), typeof(object), new[] {typeof(object), typeof(int)}, typeof(string), true);
            var il = method.GetILGenerator();
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0); // stack: [scope]
            il.Emit(System.Reflection.Emit.OpCodes.Castclass, t_DynamicScope);
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1); // stack: [scope, token]
            var getter = t_DynamicScope.GetProperty("Item", BindingFlags.Instance | BindingFlags.NonPublic).GetGetMethod(true);
            il.EmitCall(System.Reflection.Emit.OpCodes.Call, getter, null); // stack: [scope[this]]
            il.Emit(System.Reflection.Emit.OpCodes.Ret);

            return (Func<object, int, object>)method.CreateDelegate(typeof(Func<object, int, object>));
        }

        private static Func<object, MethodBase, SignatureHelper, uint> BuildGetTokenFor()
        {
            var method = new DynamicMethod(Guid.NewGuid().ToString(), typeof(uint), new[] {typeof(object), typeof(MethodBase), typeof(SignatureHelper)}, typeof(string), true);
            var il = method.GetILGenerator();

            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0); // stack: [scope]
            il.Emit(System.Reflection.Emit.OpCodes.Castclass, t_DynamicScope);
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1); // stack: [scope, method]
            il.Emit(System.Reflection.Emit.OpCodes.Castclass, t_RuntimeMethodInfo); // stack: [scope, (RuntimeMethodInfo)method]
            il.Emit(System.Reflection.Emit.OpCodes.Ldarg_2); // stack: [scope, (RuntimeMethodInfo)method, signature]

            var constructor = t_VarArgsMethod.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] {t_RuntimeMethodInfo, typeof(SignatureHelper)}, null);
            var getTokenForMethod = t_DynamicScope.GetMethod("GetTokenFor", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] {t_VarArgsMethod}, null);

            il.Emit(System.Reflection.Emit.OpCodes.Newobj, constructor); // stack: [scope, new VarArgsMethod((RuntimeMethodInfo)method, signature)]
            il.EmitCall(System.Reflection.Emit.OpCodes.Call, getTokenForMethod, null); // stack: [scope.GetTokenFor(new VarArgsMethod((RuntimeMethodInfo)method, signature))]

            il.Emit(System.Reflection.Emit.OpCodes.Ret);

            return (Func<object, MethodBase, SignatureHelper, uint>)method.CreateDelegate(typeof(Func<object, MethodBase, SignatureHelper, uint>));
        }

        public object this[int token] { get { return itemGetter(inst, token); } }

        public MetadataToken GetTokenFor(MethodBase method, SignatureHelper signature)
        {
            return new MetadataToken(getTokenFor(inst, method, signature));
        }

        public static void Init()
        {
        }

        public readonly object inst;

        private static readonly Func<object, int, object> itemGetter;
        private static readonly Func<object, MethodBase, SignatureHelper, uint> getTokenFor;
        private static readonly Type t_DynamicScope;
        private static readonly Type t_VarArgsMethod;
        private static readonly Type t_RuntimeMethodInfo;
    }
}