﻿using System;

namespace Melanchall.DryMidi
{
    internal sealed class ChannelEventReader : IEventReader
    {
        #region IEventReader

        public MidiEvent Read(MidiReader reader, ReadingSettings settings, byte currentStatusByte)
        {
            var statusByte = currentStatusByte.GetHead();
            var channel = currentStatusByte.GetTail();

            Type eventType;
            if (!StandardEventTypes.Channel.TryGetType(statusByte, out eventType))
                throw new UnknownChannelEventException(statusByte, channel);

            var channelEvent = (ChannelEvent)Activator.CreateInstance(eventType);
            channelEvent.ReadContent(reader, settings, MidiEvent.UnknownContentSize);
            channelEvent.Channel = channel;
            return channelEvent;
        }

        #endregion
    }
}