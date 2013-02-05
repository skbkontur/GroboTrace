using System;

namespace GroboTrace.Exceptions
{
    public class InaccessibleTypeException: Exception
    {
        public InaccessibleTypeException(Type type): base(string.Format("Тип {0} объявлен непубличным", type))
        {
        }
    }
}