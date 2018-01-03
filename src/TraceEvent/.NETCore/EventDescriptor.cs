#if DOTNET_CORE

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace System.Diagnostics.Eventing
{
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    internal struct EventDescriptor
    {
#region private
        [FieldOffset(0)]
        private int m_traceloggingId;
        [FieldOffset(0)]
        private ushort m_id;
        [FieldOffset(2)]
        private byte m_version;
        [FieldOffset(3)]
        private byte m_channel;
        [FieldOffset(4)]
        private byte m_level;
        [FieldOffset(5)]
        private byte m_opcode;
        [FieldOffset(6)]
        private ushort m_task;
        [FieldOffset(8)]
        private long m_keywords;
#endregion

        public EventDescriptor(
                int traceloggingId,
                byte level,
                byte opcode,
                long keywords
                )
        {
            this.m_id = 0;
            this.m_version = 0;
            this.m_channel = 0;
            this.m_traceloggingId = traceloggingId;
            this.m_level = level;
            this.m_opcode = opcode;
            this.m_task = 0;
            this.m_keywords = keywords;
        }

        public EventDescriptor(
                int id,
                byte version,
                byte channel,
                byte level,
                byte opcode,
                int task,
                long keywords
                )
        {
            m_traceloggingId = 0;
            m_id = (ushort)id;
            m_version = version;
            m_channel = channel;
            m_level = level;
            m_opcode = opcode;
            m_keywords = keywords;
            m_task = (ushort)task;
        }

        public int EventId
        {
            get
            {
                return m_id;
            }
        }
        public byte Version
        {
            get
            {
                return m_version;
            }
        }
        public byte Channel
        {
            get
            {
                return m_channel;
            }
        }
        public byte Level
        {
            get
            {
                return m_level;
            }
        }
        public byte Opcode
        {
            get
            {
                return m_opcode;
            }
        }
        public int Task
        {
            get
            {
                return m_task;
            }
        }
        public long Keywords
        {
            get
            {
                return m_keywords;
            }
        }

        public override bool Equals(object obj)
        {
            if (!(obj is EventDescriptor))
                return false;

            return Equals((EventDescriptor) obj);
        }

        public override int GetHashCode()
        {
            return m_id ^ m_version ^ m_channel ^ m_level ^ m_opcode ^ m_task ^ (int)m_keywords;
        }

        public bool Equals(EventDescriptor other)
        {
            if ((m_id != other.m_id) ||
                (m_version != other.m_version) ||
                (m_channel != other.m_channel) ||
                (m_level != other.m_level) ||
                (m_opcode != other.m_opcode) ||
                (m_task != other.m_task) ||
                (m_keywords != other.m_keywords))
            {
                return false;
            }
            return true;
        }

        public static bool operator ==(EventDescriptor event1, EventDescriptor event2)
        {
            return event1.Equals(event2);
        }

        public static bool operator !=(EventDescriptor event1, EventDescriptor event2)
        {
            return !event1.Equals(event2);
        }
    }
}
#endif