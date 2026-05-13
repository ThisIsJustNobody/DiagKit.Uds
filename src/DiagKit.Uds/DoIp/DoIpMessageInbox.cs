using System.Collections.Generic;

using DiagKit.Uds.Exceptions;

namespace DiagKit.Uds.DoIp;

internal sealed class DoIpMessageInbox
{
    private readonly Dictionary<DoIpPayloadType, Queue<Entry>> _byType = new();
    private readonly object _sync = new();
    private int _count;
    private long _nextSequence;

    public void Clear()
    {
        lock (_sync)
        {
            _byType.Clear();
            _count = 0;
        }
    }

    public void Enqueue(DoIpMessage message, int inboxLimit)
    {
        lock (_sync)
        {
            if (_count >= inboxLimit)
                throw new ProtocolException($"DoIP inbox limit {inboxLimit} exceeded while deferring {message.PayloadType}.");

            if (!_byType.TryGetValue(message.PayloadType, out var queue))
            {
                queue = new Queue<Entry>();
                _byType[message.PayloadType] = queue;
            }

            queue.Enqueue(new Entry(_nextSequence++, message));
            _count++;
        }
    }

    public bool TryTake(DoIpPayloadType[] expected, out DoIpMessage message)
    {
        lock (_sync)
        {
            DoIpPayloadType bestType = default;
            long bestSequence = long.MaxValue;
            var found = false;

            foreach (var type in expected)
            {
                if (!_byType.TryGetValue(type, out var queue) || queue.Count == 0)
                    continue;

                var candidate = queue.Peek();
                if (candidate.Sequence >= bestSequence)
                    continue;

                bestType = type;
                bestSequence = candidate.Sequence;
                found = true;
            }

            if (found)
            {
                var queue = _byType[bestType];
                message = queue.Dequeue().Message;
                if (queue.Count == 0)
                    _byType.Remove(bestType);
                _count--;
                return true;
            }
        }

        message = default;
        return false;
    }

    private readonly record struct Entry(long Sequence, DoIpMessage Message);
}
