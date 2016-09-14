using System;
using System.Collections;
using System.Reflection;
using System.Reflection.Emit;

namespace GroboTrace.MethodBodyParsing
{
    public static class FieldsExtractor
    {
        public static Func<object, object> GetExtractor(FieldInfo field)
        {
            var extractor = (Func<object, object>)extractors[field];
            if (extractor == null)
            {
                lock (extractorsLock)
                {
                    extractor = (Func<object, object>)extractors[field];
                    if (extractor == null)
                        extractors[field] = extractor = BuildExtractor(field);
                }
            }
            return extractor;
        }

        public static Action<object, object> GetSetter(FieldInfo field)
        {
            var foister = (Action<object, object>)foisters[field];
            if (foister == null)
            {
                lock (foistersLock)
                {
                    foister = (Action<object, object>)foisters[field];
                    if (foister == null)
                        foisters[field] = foister = BuildFoister(field);
                }
            }
            return foister;
        }

        public static Func<T, TResult> GetExtractor<T, TResult>(FieldInfo field)
            where T : class
        {
            var extractor = GetExtractor(field);
            return arg => (TResult)extractor(arg);
        }

        public static Func<TResult> GetExtractor<TResult>(FieldInfo field)
        {
            var extractor = GetExtractor(field);
            return () => (TResult)extractor(null);
        }

        public static Action<T, TValue> GetSetter<T, TValue>(FieldInfo field)
            where T : class
        {
            var foister = GetSetter(field);
            return (inst, value) => foister(inst, value);
        }

        public static Action<TValue> GetSetter<TValue>(FieldInfo field)
        {
            var foister = GetSetter(field);
            return value => foister(null, value);
        }

        private static Func<object, object> BuildExtractor(FieldInfo field)
        {
            var methodName = "FieldExtractor$";
            if(field.IsStatic)
                methodName += field.DeclaringType + "$";
            methodName += field.Name + "$" + Guid.NewGuid();
            var dynamicMethod = new DynamicMethod(methodName, typeof(object), new[] {typeof(object)}, typeof(FieldsExtractor), true);
            var il = dynamicMethod.GetILGenerator();
            if(!field.IsStatic)
            {
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_0);
                il.Emit(System.Reflection.Emit.OpCodes.Castclass, field.DeclaringType);
            }
            il.Emit(System.Reflection.Emit.OpCodes.Ldfld, field);
            if(field.FieldType.IsValueType)
                il.Emit(System.Reflection.Emit.OpCodes.Box, field.FieldType);
            il.Emit(System.Reflection.Emit.OpCodes.Ret);
            return (Func<object, object>)dynamicMethod.CreateDelegate(typeof(Func<object, object>));
        }

        private static Action<object, object> BuildFoister(FieldInfo field)
        {
            var methodName = "FieldFoister$";
            if (field.IsStatic)
                methodName += field.DeclaringType + "$";
            methodName += field.Name + "$" + Guid.NewGuid();
            var dynamicMethod = new DynamicMethod(methodName, typeof(void), new[] { typeof(object), typeof(object) }, typeof(FieldsExtractor), true);
            var il = dynamicMethod.GetILGenerator();
            {
                if (!field.IsStatic)
                {
                    il.Emit(System.Reflection.Emit.OpCodes.Ldarg, 0);
                    il.Emit(System.Reflection.Emit.OpCodes.Castclass, field.DeclaringType);
                }
                il.Emit(System.Reflection.Emit.OpCodes.Ldarg_1);
                if (field.FieldType.IsValueType)
                    il.Emit(System.Reflection.Emit.OpCodes.Unbox_Any, field.FieldType);
                else
                    il.Emit(System.Reflection.Emit.OpCodes.Castclass, field.FieldType);
                il.Emit(System.Reflection.Emit.OpCodes.Stfld, field);
                il.Emit(System.Reflection.Emit.OpCodes.Ret);
            }
            return (Action<object, object>)dynamicMethod.CreateDelegate(typeof(Action<object, object>));
        }

        private static readonly Hashtable extractors = new Hashtable();
        private static readonly object extractorsLock = new object();

        private static readonly Hashtable foisters = new Hashtable();
        private static readonly object foistersLock = new object();
    }
}