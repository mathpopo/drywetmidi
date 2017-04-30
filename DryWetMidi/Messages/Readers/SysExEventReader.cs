﻿using System;

namespace Melanchall.DryMidi
{
    internal sealed class SysExEventReader : IEventReader
    {
        #region IEventReader

        public MidiEvent Read(MidiReader reader, ReadingSettings settings, byte currentStatusByte)
        {
            var size = reader.ReadVlqNumber();

            //

            Type eventType;
            var midiEvent = StandardEventTypes.SysEx.TryGetType(currentStatusByte, out eventType)
                ? (SysExEvent)Activator.CreateInstance(eventType)
                : null;

            if (midiEvent == null)
                throw new InvalidOperationException("Unknown SysEx event.");

            //

            midiEvent.Completed = false;
            midiEvent.ReadContent(reader, settings, size);
            return midiEvent;
        }

        #endregion
    }
}