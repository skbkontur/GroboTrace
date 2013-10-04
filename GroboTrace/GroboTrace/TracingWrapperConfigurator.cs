using System;
using System.Collections.Generic;

namespace GroboTrace
{
    public class TracingWrapperConfigurator
    {
        public void DontWrap<T>() where T : class
        {
            forbiddenTypes.Add(typeof(T));
        }

        public void DontWrap(Type type)
        {
            if(type.IsValueType)
                throw new ArgumentException("Value types are not allowed");
            forbiddenTypes.Add(type);
        }

        public bool Forbidden(Type type)
        {
            if(forbiddenTypes.Contains(type))
                return true;
            return type.IsGenericType && forbiddenTypes.Contains(type.GetGenericTypeDefinition());
        }

        private readonly HashSet<Type> forbiddenTypes = new HashSet<Type>();
    }
}