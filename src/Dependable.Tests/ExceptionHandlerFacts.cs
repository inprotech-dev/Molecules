using Dependable.Dispatcher;
using Xunit;

namespace Dependable.Tests
{
    public class ExceptionFilterForMethodFacts
    {
        [Fact]
        public void CapturesConstantArguments()
        {
            var handler = ExceptionFilter.From<string>((c, s) => s.IndexOf('a'));

            Assert.Equal('a', handler.Arguments[0]);
        }

        [Fact]
        public void CapturesLocalVariableReferences()
        {
            var argument = 'a';

            var handler = ExceptionFilter.From<string>((c, s) => s.IndexOf(argument));

            Assert.Equal(argument, handler.Arguments[0]);
        }

        [Theory]
        [InlineData('a')]
        public void CapturesArgumentReference(char argument)
        {
            var handler = ExceptionFilter.From<string>((c, s) => s.IndexOf(argument));

            Assert.Equal(argument, handler.Arguments[0]);
        }

        [Fact]
        public void CapturesComplexPropertyReference()
        {
            var thisMethod = GetType().GetMethod("CapturesComplexPropertyReference");

            var handler = ExceptionFilter.From<string>((c, s) => s.StartsWith(thisMethod.Name));

            Assert.Equal(thisMethod.Name, handler.Arguments[0]);
        }

        [Fact]
        public void CreatesAPlaceholderForExceptionContext()
        {
            var handler = ExceptionFilter.From<ExceptionFilterForMethodFacts>((c, h) => h.Log(c));

            Assert.IsType<ExceptionContext>(handler.Arguments[0]);
        }

        [Fact]
        public void CapturesMethodName()
        {
            var handler = ExceptionFilter.From<string>((c, s) => s.IndexOf('c'));

            Assert.Equal("IndexOf", handler.Method);
        }

        [Fact]
        public void CapturesType()
        {
            var handler = ExceptionFilter.From<string>((c, s) => s.IndexOf('c'));

            Assert.Equal(typeof (string), handler.Type);
        }

        void Log(ExceptionContext context)
        {            
        }
    }
}