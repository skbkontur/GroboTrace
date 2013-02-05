using System;

namespace GroboTrace.Exceptions
{
    public class InaccessibleInterfaceException: Exception
    {
        public InaccessibleInterfaceException(Type type): base(string.Format("Интерфейс {0} объявлен непубличным", type))
        {
        }
    }
}