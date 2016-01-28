﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LinuxTracing.LinuxTraceEvent;
using Xunit;

namespace LinuxTracing.Tests
{
	public class EventParseTests
	{
		private void DoStackTraceTest(string source, bool blockedTime, List<List<string>> callerStacks)
		{
			PerfScriptEventParser parser = new PerfScriptEventParser(source, blockedTime);
			parser.Parse(null, 100, testing: true);

			for (int sample = 0; sample < parser.SampleCount; sample++)
			{
				int stackID = parser.GetStackAtSample(sample);
				for (int i = 0; i < callerStacks[sample].Count; i++)
				{
					if (stackID == -1)
					{
						Assert.Equal(callerStacks[sample][i], null);
						continue;
					}

					int actualFrame = parser.GetFrameAtStack(stackID);
					string actualName = parser.GetFrameAt(actualFrame);
					Assert.Equal(callerStacks[sample][i], actualName);
					stackID = parser.GetCallerAtStack(stackID);
				}
			}
		}

		private void HeaderTest(string source, bool blockedTime,
			string[] commands,
			int[] pids,
			int[] tids,
			int[] cpus,
			double[] times,
			int[] timeProperties,
			string[] events,
			string[] eventProperties,
			ScheduleSwitch[] switches
			)
		{
			PerfScriptEventParser parser = new PerfScriptEventParser(source, blockedTime);
			parser.Parse(null, 100, testing: true);

			// Need to make sure we have the same amount of samples
			Assert.Equal(commands.Length, parser.SampleCount);

			int schedCount = 0;

			for (int i = 0; i < parser.SampleCount; i++)
			{
				LinuxEvent linuxEvent = parser.GetLinuxEventAt(i);
				Assert.Equal(commands[i], linuxEvent.Command);
				Assert.Equal(pids[i], linuxEvent.ProcessID);
				Assert.Equal(tids[i], linuxEvent.ThreadID);
				Assert.Equal(cpus[i], linuxEvent.Cpu);
				Assert.Equal(times[i], linuxEvent.Time);
				Assert.Equal(timeProperties[i], linuxEvent.TimeProperty);
				Assert.Equal(events[i], linuxEvent.EventName);
				Assert.Equal(eventProperties[i], linuxEvent.EventProperty);

				ScheduledEvent sched = linuxEvent as ScheduledEvent;
				if (switches != null && sched != null)
				{
					ScheduleSwitch actualSwitch = sched.Switch;
					ScheduleSwitch expectedSwitch = switches[schedCount++];
					Assert.Equal(expectedSwitch.NextCommand, actualSwitch.NextCommand);
					Assert.Equal(expectedSwitch.NextPriority, actualSwitch.NextPriority);
					Assert.Equal(expectedSwitch.NextThreadID, actualSwitch.NextThreadID);
					Assert.Equal(expectedSwitch.PreviousCommand, actualSwitch.PreviousCommand);
					Assert.Equal(expectedSwitch.PreviousPriority, actualSwitch.PreviousPriority);
					Assert.Equal(expectedSwitch.PreviousState, actualSwitch.PreviousState);
					Assert.Equal(expectedSwitch.PreviousThreadID, actualSwitch.PreviousThreadID);

				}
			}

		}

		[Fact]
		public void OneStack()
		{
			string path = Constants.GetPerfDumpPath("onegeneric");
			this.DoStackTraceTest(path, blockedTime: false, callerStacks: new List<List<string>> {
				new List<string>{ "module!symbol", "Thread (0)", "comm (0)", null }
			});
		}

		[Fact]
		public void LargeStack()
		{
			string path = Constants.GetPerfDumpPath("two_small_generic");
			this.DoStackTraceTest(path, blockedTime: false, callerStacks: new List<List<string>>
			{
				new List<string> { "module!symbol", "module2!symbol2", "main!main", "Thread (0)", "comm (0)" },
				new List<string> { "module3!symbol3", "module4!symbol4", "main!main", "Thread (0)", "comm2 (0)" }
			});
		}

		[Fact]
		public void SchedStackTrace()
		{
			string path = Constants.GetPerfDumpPath("one_complete_switch");
			this.DoStackTraceTest(path, blockedTime: true, callerStacks: new List<List<string>>
			{
				new List<string> { "BLOCKED_TIME", "module!symbol", "Thread (0)", "comm1 (0)"},
				new List<string> { "BLOCKED_TIME", "module!symbol", "Thread (1)", "comm2 (1)"},
			});
		}

		[Fact]
		public void EmptyStackFrames()
		{
			string path = Constants.GetPerfDumpPath("no_stack_frames");
			this.DoStackTraceTest(path, blockedTime: false,
				callerStacks: new List<List<string>>
				{
					new List<string> { "Thread (0)", "comm (0)" },
					new List<string> { "module!symbol", "Thread (0)", "comm (0)" },
				});
		}

		[Fact]
		public void NonSchedHeader()
		{
			string path = Constants.GetPerfDumpPath("onegeneric");
			this.HeaderTest(path, blockedTime: false,
				commands: new string[] { "comm" },
				pids: new int[] { 0 },
				tids: new int[] { 0 },
				cpus: new int[] { 0 },
				times: new double[] { 0.0 },
				timeProperties: new int[] { 1 },
				events: new string[] { "event_name" },
				eventProperties: new string[] { "event_properties" },
				switches: null);
		}

		[Fact]
		public void SpaceSeparatedCommandHeader()
		{
			string path = Constants.GetPerfDumpPath("space_sep_command");
			this.HeaderTest(path, blockedTime: false,
				commands: new string[] { "comm and3 another part to the command line5 part3 part1" },
				pids: new int[] { 0 },
				tids: new int[] { 0 },
				cpus: new int[] { 0 },
				times: new double[] { 0.0 },
				timeProperties: new int[] { 1 },
				events: new string[] { "event_name" },
				eventProperties: new string[] { "event_properties" },
				switches: null);
		}

		[Fact]
		public void SchedHeader()
		{
			string path = Constants.GetPerfDumpPath("one_complete_switch");
			this.HeaderTest(path, blockedTime: true,
				commands: new string[] { "comm1", "comm2" },
				pids: new int[] { 0, 1 },
				tids: new int[] { 0, 1 },
				cpus: new int[] { 0, 1 },
				times: new double[] { 0.0, 1.0 },
				timeProperties: new int[] { 1, 1 },
				events: new string[] { "sched", "sched" },
				eventProperties: new string[] { "sched_switch: prev_comm=comm1 prev_pid=0 prev_prio=0 prev_state=S ==> next_comm=comm2 next_pid=1 next_prio=1", "sched_switch: prev_comm=comm2 prev_pid=1 prev_prio=0 prev_state=S ==> next_comm=comm1 next_pid=0 next_prio=1" },
				switches: new ScheduleSwitch[]
				{
					new ScheduleSwitch("comm1", 0, 0, 'S', "comm2", 1, 1),
					new ScheduleSwitch("comm2", 1, 0, 'S', "comm1", 0, 1)
				}
				);
		}
	}
}
