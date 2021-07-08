//     Copyright (c) Microsoft Corporation.  All rights reserved.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Tracing.Utilities
{
    /// <summary>
    /// Set of unsigned integers, memory optimized for set of indices
    /// </summary>
    /// <remarks>This class is designed for set of integer indices of wide range, for example up to 100 million, both for small and large sets.</remarks>
    /// When there are zeor or single item, data is stored in count + singleValue.
    /// When there are small number of items, data is stored in <see cref="HashSet{int}"/>, 16 bytes per item.
    /// When there are lots of items, data is stored in bitset, maxValue / 8 bytes needed.
    public class IndexSet
    {
        /// <summary>
        /// Single or max value, when bitset is not in use.
        /// </summary>
        private uint maxValue;

        /// <summary>
        /// Hashset
        /// </summary>
        private HashSet<uint> hashset;

        /// <summary>
        /// Bit set
        /// </summary>
        private byte[] bitSet;

        /// <summary>
        /// integer count, when bitset is not in use.
        /// </summary>
        private int count;

        /// <summary>
        /// Add an integer
        /// </summary>
        /// <param name="value">Integer to add</param>
        public void Add(uint value)
        {
            if (this.count == 0)
            {
                this.maxValue = value;
                this.count = 1;
            }
            else if (this.bitSet  != null)
            {
                uint index = value / 8;

                int len = this.bitSet.Length;

                if (index >= len)
                {
                    while (value >= len * 8)
                    {
                        len *= 2;
                    }

                    Array.Resize(ref this.bitSet, len);
                }

                this.bitSet[index] |= (byte)(1 << (int)(value % 8));
            }
            else
            {
                if (this.count == 1)
                {
                    this.hashset = new HashSet<uint>();

                    this.hashset.Add(this.maxValue);
                }

                if (this.hashset.Add(value))
                {
                    this.count++;
                }

                if (value > this.maxValue)
                {
                    this.maxValue = value;
                }

                // Check for conversion to bit set every 2048 elements
                if (((this.count % 2048) == 2047) && ((this.count * (16 * 8)) >= this.maxValue))
                {
                    this.ConvertoBitSet();
                }
            }
        }

        /// <summary>
        /// Check if the value is in the set
        /// </summary>
        /// <param name="value">Integer to check</param>
        /// <returns>True if in the set</returns>
        public bool Contains(uint value)
        {
            if (this.count == 0)
            {
                return false;
            }
            else if (this.count == 1)
            {
                return this.maxValue == value;
            }
            else if (this.bitSet != null)
            {
                uint index = value / 8;

                if (index < this.bitSet.Length)
                {
                    return (this.bitSet[index] & ((1 << (int)(value % 8)))) != 0;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return this.hashset.Contains(value);
            }
        }

        /// <summary>
        /// Convert to bitset
        /// </summary>
        private void ConvertoBitSet()
        {
            uint len = (this.maxValue + 7) / 8;

            this.bitSet = new byte[len];

            foreach (uint v in this.hashset)
            {
                this.bitSet[v / 8] |= (byte)((1 << (int)(v % 8)));
            }

            this.hashset = null;
        }
    }
}
