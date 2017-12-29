using System;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Contrib.Tests
{
    public class RetrySpec : Akka.TestKit.Xunit2.TestKit
    {
        private static readonly Result<int> FailedElement = Result.Failure<int>(new Exception("cooked failure"));

        private static Flow<Tuple<int, T>, Tuple<Result<int>, T>, NotUsed> RetryFlow<T>() =>
            Flow.Identity<Tuple<int, T>>()
                .Select(t => t.Item1 % 2 == 0
                    ? Tuple.Create(FailedElement, t.Item2)
                    : Tuple.Create(Result.Success(t.Item1 + 1), t.Item2));

        [Fact]
        public void Retry_should_retry_ints_according_to_their_parity()
        {
            var t = this.SourceProbe<int>()
                .Select(i => Tuple.Create(i, i))
                .Via(Retry.Create(RetryFlow<int>(), s => s < 42 ? Tuple.Create(s + 1, s + 1) : null))
                .ToMaterialized(this.SinkProbe<Tuple<Result<int>, int>>(), Keep.Both)
                .Run(Sys.Materializer());

            var source = t.Item1;
            var sink = t.Item2;

            sink.Request(99);
            source.SendNext(1);
            sink.ExpectNext().Item1.Value.Should().Be(2);
            source.SendNext(2);
            sink.ExpectNext().Item1.Value.Should().Be(4);
            source.SendNext(42);
            sink.ExpectNext().Item1.Should().Be(FailedElement);
            source.SendComplete();
            sink.ExpectComplete();
        }


        [Fact]
        public void RetryConcat_should_swallow_failed_elements_that_are_retried_with_an_empty_seq()
        {
            var t = this.SourceProbe<int>()
                .Select(i => Tuple.Create(i, i))
                .Via(Retry.Concat(100, RetryFlow<int>(), _ => Enumerable.Empty<Tuple<int, int>>()))
                .ToMaterialized(this.SinkProbe<Tuple<Result<int>, int>>(), Keep.Both)
                .Run(Sys.Materializer());
            var source = t.Item1;
            var sink = t.Item2;

            sink.Request(99);
            source.SendNext(1);
            sink.ExpectNext().Item1.Value.Should().Be(2);
            source.SendNext(2);
            sink.ExpectNoMsg();
            source.SendNext(3);
            sink.ExpectNext().Item1.Value.Should().Be(4);
            source.SendNext(4);
            sink.ExpectNoMsg();
            source.SendComplete();
            sink.ExpectComplete();
        }
    }
}
