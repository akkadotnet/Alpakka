using Akka.Streams.SignalR.Internals;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.SignalR.Tests
{
    public class FixedSizeBufferTests
    {
        private readonly FixedSizeBuffer<int> buffer = new FixedSizeBuffer<int>(3);

        [Fact]
        public void FixedSizeBuffer_should_allow_to_enqueue_single_element()
        {
            buffer.TryEnqueue(1).Should().BeTrue();
        }

        [Fact]
        public void FixedSizeBuffer_should_not_allow_to_enqueue_beyond_capacity()
        {
            buffer.TryEnqueue(1).Should().BeTrue();
            buffer.TryEnqueue(2).Should().BeTrue();
            buffer.TryEnqueue(3).Should().BeTrue();
            buffer.TryEnqueue(4).Should().BeFalse();
        }

        [Fact]
        public void FixedSizeBuffer_should_not_allow_to_dequeue_empty_queue()
        {
            int msg;
            buffer.TryDequeue(out msg).Should().BeFalse();
        }

        [Fact]
        public void FixedSizeBuffer_should_allow_to_dequeue_non_empty_queue()
        {
            buffer.TryEnqueue(1).Should().BeTrue();

            int msg;
            buffer.TryDequeue(out msg).Should().BeTrue();
            msg.Should().Be(1);
        }

        [Fact]
        public void FixedSizeBuffer_should_not_allow_to_dequeue_queue_beyond_write_point()
        {
            buffer.TryEnqueue(1).Should().BeTrue();
            buffer.TryEnqueue(2).Should().BeTrue();
            buffer.TryEnqueue(3).Should().BeTrue();

            int msg;
            buffer.TryDequeue(out msg).Should().BeTrue();
            msg.Should().Be(1);
            buffer.TryDequeue(out msg).Should().BeTrue();
            msg.Should().Be(2);
            buffer.TryDequeue(out msg).Should().BeTrue();
            msg.Should().Be(3);
            buffer.TryDequeue(out msg).Should().BeFalse();

            buffer.TryEnqueue(4).Should().BeTrue();
            buffer.TryEnqueue(5).Should().BeTrue();

            buffer.TryDequeue(out msg).Should().BeTrue();
            msg.Should().Be(4);
            buffer.TryDequeue(out msg).Should().BeTrue();
            msg.Should().Be(5);
        }
    }
}