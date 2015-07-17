using NUnit.Framework;

namespace Tests
{
    public class TestBoxEventRepository : TestBase
    {
        [Test]
        public void Test()
        {
            Create<IOldBoxEventRepository, OldBoxEventRepository>();
        }

        public interface IAbstractBoxEventRepository<out TBoxEvent, in TEventContentBase>
            where TBoxEvent : class
        {
            string AddEvent<TEventContent>(string boxId, TEventContent eventContent) where TEventContent : TEventContentBase;
        }

        public abstract class AbstractBoxEventRepository<TBoxEvent, TEventContentBase> : IAbstractBoxEventRepository<TBoxEvent, TEventContentBase>
            where TBoxEvent : class
            where TEventContentBase : class
        {
            public string AddEvent<TEventContent>(string boxId, TEventContent eventContent) where TEventContent : TEventContentBase
            {
                throw new System.NotImplementedException();
            }
        }

        public class OldBoxEvent
        {
        }

        public interface IOldBoxEventContent
        {
        }

        public interface IOldBoxEventRepository : IAbstractBoxEventRepository<OldBoxEvent, IOldBoxEventContent>
        {
        }

        public class OldBoxEventRepository : AbstractBoxEventRepository<OldBoxEvent, IOldBoxEventContent>, IOldBoxEventRepository
        {
        }
    }
}