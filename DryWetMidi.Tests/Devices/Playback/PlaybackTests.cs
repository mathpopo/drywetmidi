﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Devices;
using Melanchall.DryWetMidi.Smf;
using Melanchall.DryWetMidi.Smf.Interaction;
using Melanchall.DryWetMidi.Tests.Smf.Interaction;
using Melanchall.DryWetMidi.Tests.Utilities;
using NUnit.Framework;

namespace Melanchall.DryWetMidi.Tests.Devices
{
    [TestFixture]
    public sealed class PlaybackTests
    {
        #region Nested classes

        private sealed class PlaybackContext
        {
            public List<ReceivedEvent> ReceivedEvents { get; } = new List<ReceivedEvent>();

            public List<SentEvent> SentEvents { get; } = new List<SentEvent>();

            public object ReceivedEventsLockObject { get; } = new object();

            public Stopwatch Stopwatch { get; } = new Stopwatch();

            public TempoMap TempoMap { get; } = TempoMap.Default;

            public List<TimeSpan> ExpectedTimes { get; } = new List<TimeSpan>();
        }

        #endregion

        #region Delegates

        private delegate void PlaybackAction(PlaybackContext context, Playback playback);

        #endregion

        #region Test methods

        [TestCase(1.0)]
        [TestCase(2.0)]
        [TestCase(0.5)]
        public void CheckPlayback_NonBlocking(double speed)
        {
            var eventsToSend = new[]
            {
                new EventToSend(new NoteOnEvent((SevenBitNumber)100, (SevenBitNumber)20) { Channel = (FourBitNumber)5 }, TimeSpan.Zero),
                new EventToSend(new NoteOffEvent((SevenBitNumber)100, (SevenBitNumber)10) { Channel = (FourBitNumber)5 }, TimeSpan.FromSeconds(2)),
                new EventToSend(new NoteOnEvent(), TimeSpan.FromSeconds(1)),
                new EventToSend(new NoteOnEvent((SevenBitNumber)30, (SevenBitNumber)50), TimeSpan.Zero),
                new EventToSend(new NoteOffEvent(), TimeSpan.FromSeconds(3)),
                new EventToSend(new NoteOffEvent((SevenBitNumber)30, (SevenBitNumber)50), TimeSpan.Zero)
            };

            CheckPlayback(
                eventsToSend,
                speed,
                beforePlaybackStarted: (context, playback) => { },
                startPlayback: (context, playback) => playback.Start(),
                afterPlaybackStarted: (context, playback) =>
                {
                    Assert.LessOrEqual(context.Stopwatch.Elapsed, SendReceiveUtilities.MaximumEventSendReceiveDelay, "Playback blocks current thread.");
                    Assert.IsTrue(playback.IsRunning, "Playback is not running after start.");
                },
                waiting: (context, playback) =>
                {
                    var timeout = context.ExpectedTimes.Last() + SendReceiveUtilities.MaximumEventSendReceiveDelay;
                    var areEventsReceived = SpinWait.SpinUntil(() => context.ReceivedEvents.Count == eventsToSend.Length, timeout);
                    Assert.IsTrue(areEventsReceived, $"Events are not received for timeout {timeout}.");
                },
                finalChecks: (context, playback) =>
                {
                    var playbackStopped = SpinWait.SpinUntil(() => !playback.IsRunning, SendReceiveUtilities.MaximumEventSendReceiveDelay);
                    Assert.IsTrue(playbackStopped, "Playback is running after completed.");
                });
        }

        [TestCase(1.0)]
        [TestCase(2.0)]
        [TestCase(0.5)]
        public void CheckPlayback_Blocking(double speed)
        {
            var eventsToSend = new[]
            {
                new EventToSend(new NoteOnEvent((SevenBitNumber)100, (SevenBitNumber)20) { Channel = (FourBitNumber)5 }, TimeSpan.Zero),
                new EventToSend(new NoteOffEvent((SevenBitNumber)100, (SevenBitNumber)10) { Channel = (FourBitNumber)5 }, TimeSpan.FromSeconds(2)),
                new EventToSend(new NoteOnEvent(), TimeSpan.FromSeconds(1)),
                new EventToSend(new NoteOnEvent((SevenBitNumber)30, (SevenBitNumber)50), TimeSpan.Zero),
                new EventToSend(new NoteOffEvent(), TimeSpan.FromSeconds(3)),
                new EventToSend(new NoteOffEvent((SevenBitNumber)30, (SevenBitNumber)50), TimeSpan.Zero)
            };

            CheckPlayback(
                eventsToSend,
                speed,
                beforePlaybackStarted: (context, playback) => { },
                startPlayback: (context, playback) => playback.Play(),
                afterPlaybackStarted: (context, playback) =>
                {
                    Assert.GreaterOrEqual(context.Stopwatch.Elapsed, context.ExpectedTimes.Last(), "Playback doesn't block current thread.");
                },
                waiting: (context, playback) =>
                {
                    var areEventsReceived = SpinWait.SpinUntil(() => context.ReceivedEvents.Count == eventsToSend.Length, SendReceiveUtilities.MaximumEventSendReceiveDelay);
                    Assert.IsTrue(areEventsReceived, $"Events are not received.");
                },
                finalChecks: (context, playback) => { });
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(10)]
        public void CheckPlaybackLooping(int repetitionsNumber)
        {
            var eventsToSend = new[]
            {
                new EventToSend(new NoteOnEvent((SevenBitNumber)100, (SevenBitNumber)20) { Channel = (FourBitNumber)5 }, TimeSpan.Zero),
                new EventToSend(new NoteOffEvent((SevenBitNumber)100, (SevenBitNumber)10) { Channel = (FourBitNumber)5 }, TimeSpan.FromSeconds(2)),
                new EventToSend(new NoteOnEvent(), TimeSpan.FromSeconds(1)),
                new EventToSend(new NoteOnEvent((SevenBitNumber)30, (SevenBitNumber)50), TimeSpan.Zero),
                new EventToSend(new NoteOffEvent(), TimeSpan.FromSeconds(3)),
                new EventToSend(new NoteOffEvent((SevenBitNumber)30, (SevenBitNumber)50), TimeSpan.Zero)
            };

            CheckPlayback(
                eventsToSend,
                speed: 1.0,
                beforePlaybackStarted: (context, playback) =>
                {
                    var originalExpectedTimes = context.ExpectedTimes.ToList();

                    for (int i = 1; i < repetitionsNumber; i++)
                    {
                        var lastTime = context.ExpectedTimes.Last();
                        context.ExpectedTimes.AddRange(originalExpectedTimes.Select(t => lastTime + t));
                    }

                    playback.Loop = true;
                },
                startPlayback: (context, playback) => playback.Start(),
                afterPlaybackStarted: (context, playback) =>
                {
                    Assert.LessOrEqual(context.Stopwatch.Elapsed, SendReceiveUtilities.MaximumEventSendReceiveDelay, "Playback blocks current thread.");
                    Assert.IsTrue(playback.IsRunning, "Playback is not running after start.");
                },
                waiting: (context, playback) =>
                {
                    var timeout = context.ExpectedTimes.Last() + SendReceiveUtilities.MaximumEventSendReceiveDelay;
                    var areEventsReceived = SpinWait.SpinUntil(() => context.ReceivedEvents.Count >= eventsToSend.Length * repetitionsNumber, timeout);
                    Assert.IsTrue(areEventsReceived, $"Events are not received for timeout {timeout}.");
                },
                finalChecks: (context, playback) =>
                {
                    Assert.IsTrue(playback.IsRunning, "Playback is not running.");
                    playback.Stop();
                    Assert.IsFalse(playback.IsRunning, "Playback is running after stop.");

                    lock (context.ReceivedEventsLockObject)
                    {
                        var groupedReceivedEvents = context.ReceivedEvents.GroupBy(e => e.Event, new MidiEventEquality.EqualityComparer(false)).Take(eventsToSend.Length).ToArray();
                        Assert.IsTrue(groupedReceivedEvents.All(g => g.Count() >= repetitionsNumber), $"Events are not repeated {repetitionsNumber} times.");
                    }
                });
        }

        [Test]
        public void CheckPlaybackStop()
        {
            var eventsToSend = new[]
            {
                new EventToSend(new NoteOnEvent((SevenBitNumber)100, (SevenBitNumber)20) { Channel = (FourBitNumber)5 }, TimeSpan.Zero),
                new EventToSend(new NoteOffEvent((SevenBitNumber)100, (SevenBitNumber)10) { Channel = (FourBitNumber)5 }, TimeSpan.FromSeconds(2)),
                new EventToSend(new NoteOnEvent(), TimeSpan.FromSeconds(1)),
                new EventToSend(new NoteOnEvent((SevenBitNumber)30, (SevenBitNumber)50), TimeSpan.Zero),
                new EventToSend(new NoteOffEvent(), TimeSpan.FromSeconds(3)),
                new EventToSend(new NoteOffEvent((SevenBitNumber)30, (SevenBitNumber)50), TimeSpan.Zero)
            };

            CheckPlaybackStop(
                eventsToSend,
                eventsWillBeSent: eventsToSend,
                stopAfter: TimeSpan.FromMilliseconds(2500),
                stopPeriod: TimeSpan.FromSeconds(3),
                setupPlayback: (context, playback) => { },
                afterStart: (context, playback) => { },
                afterStop: (context, playback) => { },
                afterResume: (context, playback) => { });
        }

        [Test]
        public void CheckNoteStop_Interrupt()
        {
            CheckPlaybackStop(
                eventsToSend: new[]
                {
                    new EventToSend(new NoteOnEvent(), TimeSpan.Zero),
                    new EventToSend(new NoteOffEvent(), TimeSpan.FromSeconds(5))
                },
                eventsWillBeSent: new[]
                {
                    new EventToSend(new NoteOnEvent(), TimeSpan.Zero),
                    new EventToSend(new NoteOffEvent(), TimeSpan.FromSeconds(1)),
                    new EventToSend(new NoteOffEvent(), TimeSpan.FromSeconds(4))
                },
                stopAfter: TimeSpan.FromSeconds(1),
                stopPeriod: TimeSpan.FromSeconds(2),
                setupPlayback: (context, playback) => playback.NoteStopPolicy = NoteStopPolicy.Interrupt,
                afterStart: (context, playback) => { },
                afterStop: (context, playback) => { },
                afterResume: (context, playback) => { });
        }

        [Test]
        public void CheckNoteStop_Hold()
        {
            var eventsToSend = new[]
            {
                new EventToSend(new NoteOnEvent(), TimeSpan.Zero),
                new EventToSend(new NoteOffEvent(), TimeSpan.FromSeconds(5))
            };

            CheckPlaybackStop(
                eventsToSend,
                eventsWillBeSent: eventsToSend,
                stopAfter: TimeSpan.FromSeconds(1),
                stopPeriod: TimeSpan.FromSeconds(2),
                setupPlayback: (context, playback) => playback.NoteStopPolicy = NoteStopPolicy.Hold,
                afterStart: (context, playback) => { },
                afterStop: (context, playback) => { },
                afterResume: (context, playback) => { });
        }

        [Test]
        public void CheckNoteStop_Split()
        {
            CheckPlaybackStop(
                eventsToSend: new[]
                {
                    new EventToSend(new NoteOnEvent(), TimeSpan.Zero),
                    new EventToSend(new NoteOffEvent(), TimeSpan.FromSeconds(5))
                },
                eventsWillBeSent: new[]
                {
                    new EventToSend(new NoteOnEvent(), TimeSpan.Zero),
                    new EventToSend(new NoteOffEvent(), TimeSpan.FromSeconds(1)),
                    new EventToSend(new NoteOnEvent(), TimeSpan.FromTicks(1)),
                    new EventToSend(new NoteOffEvent(), TimeSpan.FromSeconds(4))
                },
                stopAfter: TimeSpan.FromSeconds(1),
                stopPeriod: TimeSpan.FromSeconds(2),
                setupPlayback: (context, playback) => playback.NoteStopPolicy = NoteStopPolicy.Split,
                afterStart: (context, playback) => { },
                afterStop: (context, playback) => { },
                afterResume: (context, playback) => { });
        }

        [Test]
        public void GetCurrentTime()
        {
            var eventsToSend = new[]
            {
                new EventToSend(new NoteOnEvent(), TimeSpan.Zero),
                new EventToSend(new NoteOffEvent(), TimeSpan.FromSeconds(10))
            };

            var stopAfter = TimeSpan.FromSeconds(2);
            var stopPeriod = TimeSpan.FromSeconds(2);

            CheckPlaybackStop(
                eventsToSend,
                eventsWillBeSent: eventsToSend,
                stopAfter: stopAfter,
                stopPeriod: stopPeriod,
                setupPlayback: (context, playback) => { },
                afterStart: (context, playback) => CheckCurrentTime(playback, TimeSpan.Zero, "started"),
                afterStop: (context, playback) => CheckCurrentTime(playback, stopAfter, "stopped"),
                afterResume: (context, playback) => CheckCurrentTime(playback, stopAfter, "resumed"),
                runningAfterResume: new Tuple<TimeSpan, PlaybackAction>[]
                {
                    Tuple.Create<TimeSpan, PlaybackAction>(TimeSpan.FromSeconds(1), (context, playback) => CheckCurrentTime(playback, stopAfter + TimeSpan.FromSeconds(1), "resumed")),
                    Tuple.Create<TimeSpan, PlaybackAction>(TimeSpan.FromSeconds(2), (context, playback) => CheckCurrentTime(playback, stopAfter + TimeSpan.FromSeconds(3), "resumed"))
                });
        }

        [Test]
        public void MoveToStart()
        {
            var stopAfter = TimeSpan.FromSeconds(2);
            var stopPeriod = TimeSpan.FromSeconds(2);

            CheckPlaybackStop(
                eventsToSend: new[]
                {
                    new EventToSend(new NoteOnEvent(), TimeSpan.Zero),
                    new EventToSend(new NoteOffEvent(), TimeSpan.FromSeconds(10))
                },
                eventsWillBeSent: new EventToSend[] { },
                stopAfter: stopAfter,
                stopPeriod: stopPeriod,
                setupPlayback: (context, playback) => { },
                afterStart: (context, playback) => { },
                afterStop: (context, playback) => playback.MoveToStart(),
                afterResume: (context, playback) => CheckCurrentTime(playback, TimeSpan.Zero, "stopped"),
                runningAfterResume: new Tuple<TimeSpan, PlaybackAction>[]
                {
                    Tuple.Create<TimeSpan, PlaybackAction>(TimeSpan.FromSeconds(1), (context, playback) => CheckCurrentTime(playback, TimeSpan.FromSeconds(1), "resumed")),
                    Tuple.Create<TimeSpan, PlaybackAction>(TimeSpan.FromSeconds(2), (context, playback) =>
                    {
                        playback.MoveToStart();
                        CheckCurrentTime(playback, TimeSpan.Zero, "resumed");
                    }),
                    Tuple.Create<TimeSpan, PlaybackAction>(TimeSpan.FromSeconds(2), (context, playback) => CheckCurrentTime(playback, TimeSpan.FromSeconds(2), "resumed"))
                },
                explicitExpectedTimes: new[]
                {
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(4),
                    TimeSpan.FromSeconds(7),
                    TimeSpan.FromSeconds(17)
                });
        }

        #endregion

        #region Private methods

        private void CheckPlayback(
            ICollection<EventToSend> eventsToSend,
            double speed,
            PlaybackAction beforePlaybackStarted,
            PlaybackAction startPlayback,
            PlaybackAction afterPlaybackStarted,
            PlaybackAction waiting,
            PlaybackAction finalChecks)
        {
            var playbackContext = new PlaybackContext();

            var receivedEvents = playbackContext.ReceivedEvents;
            var sentEvents = playbackContext.SentEvents;
            var stopwatch = playbackContext.Stopwatch;
            var tempoMap = playbackContext.TempoMap;

            var eventsForPlayback = new List<MidiEvent>();
            var expectedTimes = playbackContext.ExpectedTimes;
            var currentTime = TimeSpan.Zero;

            foreach (var eventToSend in eventsToSend)
            {
                var midiEvent = eventToSend.Event.Clone();
                midiEvent.DeltaTime = LengthConverter.ConvertFrom((MetricTimeSpan)eventToSend.Delay, (MetricTimeSpan)currentTime, tempoMap);
                currentTime += eventToSend.Delay;
                eventsForPlayback.Add(midiEvent);
                expectedTimes.Add(TimeSpan.FromTicks(MathUtilities.RoundToLong(currentTime.Ticks / speed)));
            }

            using (var outputDevice = OutputDevice.GetByName(MidiDevicesNames.DeviceA))
            {
                SendReceiveUtilities.WarmUpDevice(outputDevice);
                outputDevice.EventSent += (_, e) => sentEvents.Add(new SentEvent(e.Event, stopwatch.Elapsed));

                using (var playback = new Playback(eventsForPlayback, tempoMap, outputDevice))
                {
                    playback.Speed = speed;
                    beforePlaybackStarted(playbackContext, playback);

                    using (var inputDevice = InputDevice.GetByName(MidiDevicesNames.DeviceA))
                    {
                        inputDevice.EventReceived += (_, e) =>
                        {
                            lock (playbackContext.ReceivedEventsLockObject)
                            {
                                receivedEvents.Add(new ReceivedEvent(e.Event, stopwatch.Elapsed));
                            }
                        };
                        inputDevice.StartEventsListening();
                        stopwatch.Start();

                        startPlayback(playbackContext, playback);
                        afterPlaybackStarted(playbackContext, playback);
                        waiting(playbackContext, playback);

                        stopwatch.Stop();

                        finalChecks(playbackContext, playback);
                    }
                }
            }

            CompareSentReceivedEvents(sentEvents.Take(expectedTimes.Count).ToList(), receivedEvents.Take(expectedTimes.Count).ToList(), expectedTimes);
        }

        private void CheckPlaybackStop(
            ICollection<EventToSend> eventsToSend,
            ICollection<EventToSend> eventsWillBeSent,
            TimeSpan stopAfter,
            TimeSpan stopPeriod,
            PlaybackAction setupPlayback,
            PlaybackAction afterStart,
            PlaybackAction afterStop,
            PlaybackAction afterResume,
            IEnumerable<Tuple<TimeSpan, PlaybackAction>> runningAfterResume = null,
            ICollection<TimeSpan> explicitExpectedTimes = null)
        {
            var playbackContext = new PlaybackContext();

            var receivedEvents = playbackContext.ReceivedEvents;
            var sentEvents = playbackContext.SentEvents;
            var stopwatch = playbackContext.Stopwatch;
            var tempoMap = playbackContext.TempoMap;

            var eventsForPlayback = new List<MidiEvent>();
            var expectedTimes = playbackContext.ExpectedTimes;
            var currentTime = TimeSpan.Zero;

            foreach (var eventToSend in eventsToSend)
            {
                var midiEvent = eventToSend.Event.Clone();
                midiEvent.DeltaTime = LengthConverter.ConvertFrom((MetricTimeSpan)eventToSend.Delay, (MetricTimeSpan)currentTime, tempoMap);
                currentTime += eventToSend.Delay;
                eventsForPlayback.Add(midiEvent);
            }

            if (explicitExpectedTimes != null)
                expectedTimes.AddRange(explicitExpectedTimes);
            else
            {
                currentTime = TimeSpan.Zero;
                foreach (var eventWillBeSent in eventsWillBeSent)
                {
                    currentTime += eventWillBeSent.Delay;
                    expectedTimes.Add(currentTime > stopAfter ? currentTime + stopPeriod : currentTime);
                }
            }

            using (var outputDevice = OutputDevice.GetByName(MidiDevicesNames.DeviceA))
            {
                SendReceiveUtilities.WarmUpDevice(outputDevice);
                outputDevice.EventSent += (_, e) => sentEvents.Add(new SentEvent(e.Event, stopwatch.Elapsed));

                using (var playback = new Playback(eventsForPlayback, tempoMap, outputDevice))
                {
                    setupPlayback(playbackContext, playback);

                    using (var inputDevice = InputDevice.GetByName(MidiDevicesNames.DeviceA))
                    {
                        inputDevice.EventReceived += (_, e) =>
                        {
                            lock (playbackContext.ReceivedEventsLockObject)
                            {
                                receivedEvents.Add(new ReceivedEvent(e.Event, stopwatch.Elapsed));
                            }
                        };
                        inputDevice.StartEventsListening();
                        stopwatch.Start();
                        playback.Start();

                        afterStart(playbackContext, playback);

                        SpinWait.SpinUntil(() => stopwatch.Elapsed >= stopAfter);
                        playback.Stop();

                        afterStop(playbackContext, playback);

                        Thread.Sleep(stopPeriod);
                        playback.Start();

                        afterResume(playbackContext, playback);

                        if (runningAfterResume != null)
                        {
                            foreach (var check in runningAfterResume)
                            {
                                Thread.Sleep(check.Item1);
                                check.Item2(playbackContext, playback);
                            }
                        }

                        var timeout = expectedTimes.Last() + SendReceiveUtilities.MaximumEventSendReceiveDelay;
                        var areEventsReceived = SpinWait.SpinUntil(() => receivedEvents.Count == expectedTimes.Count, timeout);
                        Assert.IsTrue(areEventsReceived, $"Events are not received for timeout {timeout}.");

                        stopwatch.Stop();

                        var playbackStopped = SpinWait.SpinUntil(() => !playback.IsRunning, SendReceiveUtilities.MaximumEventSendReceiveDelay);
                        Assert.IsTrue(playbackStopped, "Playback is running after completed.");
                    }
                }
            }

            CompareSentReceivedEvents(sentEvents, receivedEvents, expectedTimes);
        }

        private void CompareSentReceivedEvents(
            IReadOnlyList<SentEvent> sentEvents,
            IReadOnlyList<ReceivedEvent> receivedEvents,
            IReadOnlyList<TimeSpan> expectedTimes)
        {
            for (var i = 0; i < sentEvents.Count; i++)
            {
                var sentEvent = sentEvents[i];
                var receivedEvent = receivedEvents[i];
                var expectedTime = expectedTimes[i];

                Assert.IsTrue(
                    MidiEventEquality.AreEqual(sentEvent.Event, receivedEvent.Event, false),
                    $"Received event {receivedEvent.Event} doesn't match sent one {sentEvent.Event}.");

                var offsetFromExpectedTime = (sentEvent.Time - expectedTime).Duration();
                Assert.LessOrEqual(
                    offsetFromExpectedTime,
                    SendReceiveUtilities.MaximumEventSendReceiveDelay,
                    $"Event was sent too late (at {sentEvent.Time} instead of {expectedTime}).");
            }
        }

        private static void CheckCurrentTime(Playback playback, TimeSpan expectedCurrentTime, string afterPlaybackAction)
        {
            TimeSpan currentTime = (MetricTimeSpan)playback.GetCurrentTime(TimeSpanType.Metric);
            Assert.IsTrue(
                AreTimeSpansEqual(currentTime, expectedCurrentTime),
                $"Current time ({currentTime}) is invalid after playback {afterPlaybackAction} ({expectedCurrentTime}).");
        }

        private static bool AreTimeSpansEqual(TimeSpan timeSpan1, TimeSpan timeSpan2)
        {
            // TODO: decrease epsilon
            var epsilon = TimeSpan.FromMilliseconds(15);
            var delta = (timeSpan1 - timeSpan2).Duration();
            return delta <= epsilon;
        }

        #endregion
    }
}