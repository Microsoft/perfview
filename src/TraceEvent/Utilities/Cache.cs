using System;
using System.Diagnostics;

namespace Utilities
{
    /// <summary>
    /// A finite cache based with a least recently used algorithm for replacement.   
    /// It is meant to be fast (fast as a hashtable), and space efficient (not much
    /// over the MaxEntry key-value pairs are stored.  (only 8 bytes per entry additional).  
    /// 
    /// After reaching MaxEntry entries.  It uses a roughly least-recently used
    /// algorithm to pick a entry to recycle.    To stay efficient it only searches
    /// a finite time (up to 5 entries) for a entry that is older than 1/2 of the
    /// entries in the table.  
    /// 
    /// It has the property that if you are in the maxEntries/2 most commonly fetched
    /// things, you very unlikely to be evicted once you are in the cache.   
    /// </summary>
#if UTILITIES_PUBLIC
    public 
#endif
    internal class Cache<K, T> where T : class where K : IEquatable<K>
    {
        /// <summary>
        /// maxEntries currently is only set in the constructor.   Thus this is a finite sized cache
        /// but is otherwise very efficient.  Currently it uses ushorts internally so the number
        /// of entries is limited to 64K (it silently limits it if you give maxEntries > 64K).  
        /// </summary>
        /// <param name="maxEntries"></param>
        public Cache(int maxEntries)
        {
            // We use ushorts in the implementation, so make sure we are in the regime that we can represent.  
            if (maxEntries > 0xFFFE)
            {
                maxEntries = 0xFFFE;
            }

            // The Hash table is only ushorts, so it is OK to have a bigger table.   
            var hashEntries = maxEntries * 2 + 1;

            m_hashTable = new ushort[hashEntries];
            m_entries = new CacheEntry[maxEntries];
            m_curAge = 2; // Make all entries (which have age 0, old).  
            Clear();
        }

        /// <summary>
        /// Fetches the value from the cache with key 'key'.  Returns default(T) if not present
        /// </summary>
        public T Get(K key)
        {
            T retVal;
            TryGet(key, out retVal);
            return retVal;
        }

        /// <summary>
        /// Fetches the value from the cache with key 'key'.  Returns false if not present.
        /// </summary>
        public bool TryGet(K key, out T valueRet)
        {
            int hash = key.GetHashCode();
            uint tableIndex = unchecked((uint)((uint)hash % (uint)m_hashTable.Length));
            int entryIndex = m_hashTable[tableIndex];
            int entryHash = (hash ^ (hash >> 16)) & HashMask;
            for (; ; )
            {
                if (entryIndex == End)
                {
                    valueRet = default(T);
                    return false;
                }
                CacheEntry entry = m_entries[entryIndex];
                if (entry.Hash == entryHash && entry.Key.Equals(key))
                {
                    // Update the age of the entry (to allow pseudo-least-recently used recycling)
                    if (entry.Age != m_curAge)
                    {
                        UpdateAge(ref m_entries[entryIndex]);
                    }

                    valueRet = entry.Value;
                    return true;
                }
                entryIndex = entry.Next;
            }
        }

        /// <summary>
        /// Adds 'key' with value 'value' to the cache. 
        /// </summary>
        public void Add(K key, T value)
        {
            int hash = key.GetHashCode();
            uint tableIndex = unchecked((uint)((uint)hash % (uint)m_hashTable.Length));
            int entryHash = (hash ^ (hash >> 16)) & HashMask;

            ushort entryIndex = GetFreeEntry();
            m_entries[entryIndex] = new CacheEntry()
            {
                Hash = entryHash,
                Key = key,
                Value = value,
                Next = m_hashTable[tableIndex]
            };
            UpdateAge(ref m_entries[entryIndex]);
            m_hashTable[tableIndex] = entryIndex;
        }

        /// <summary>
        /// Removes all entries in the cache.  
        /// </summary>
        public void Clear()
        {
            // Initialize the has table to be empty (pointers pointing to 'End')
            for (int i = 0; i < m_hashTable.Length; i++)
            {
                m_hashTable[i] = End;
            }

            // Null out values so that we release the memory.   
            for (int i = 0; i < m_entries.Length; i++)
            {
                m_entries[i].Key = default(K);
                if (m_entries[i].Value != null && m_entries[i].Value is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                m_entries[i].Value = null;
            }
            // indicate the free entries. 
            m_freeEntries = (ushort)m_entries.Length;
        }

        /// <summary>
        /// Sets the maxiumum number of key-value pairs the cache will keep.  (after that old ones are remvoed). 
        /// </summary>
        public int MaxEntries
        {
            get { return m_entries.Length; }
        }

        #region private 
        private void UpdateAge(ref CacheEntry entry)
        {
            entry.Age = m_curAge;
            // When we have moved 1/2 of all entries to the current age, create a new age.  
            m_entriesInCurAge++;
            if (m_entriesInCurAge * 2 >= m_entries.Length)
            {
                m_entriesInCurAge = 0;
                m_curAge = (byte)((m_curAge + 1) & AgeMask);
            }
        }

        // Finds an free entry in the table and returns the index to it.  
        private ushort GetFreeEntry()
        {
            // Easy case, we still have some from the last Clear() operation.  
            if (0 < m_freeEntries)
            {
                return --m_freeEntries;
            }

            // Find an old entry to recycle.  
            int tries = 0;
            for (; ; )
            {
                // Continue the scan where we left off and simply free the next non-empty
                // hash chain we encounter.  
                ushort cur = m_hashTable[m_freeScan];
                ushort prev = End;
                for (; ; )
                {
                    if (cur == End)
                    {
                        break;
                    }

                    // Look for an older age (0 means current age, 1 means
                    // it is in the older epoc.  After a number of trials we simply
                    // steal one, and the unlucky entry gets evicited 'unfairly'. 
                    // We don't care about wrap around as that is rare and it just
                    // means that it has to wait until the next epoc to die.  
                    int age = (m_curAge - m_entries[cur].Age) & AgeMask;
                    if (age > 1 || tries >= 5)
                    {
                        // Remove cur 
                        if (prev == End)
                        {
                            m_hashTable[m_freeScan] = m_entries[cur].Next;
                        }
                        else
                        {
                            m_entries[prev].Next = m_entries[cur].Next;
                        }

                        // Note that because we don't advance m_freeScan, we will
                        // scan the elements in the front of this chain again but
                        // that is OK.  
                        return cur;
                    }
                    tries++;
                    prev = cur;
                    cur = m_entries[cur].Next;
                }
                m_freeScan++;
                if (m_freeScan >= m_hashTable.Length)
                {
                    m_freeScan = 0;
                }
            }
        }

        // Holds a entry in our open hash table.  
        private struct CacheEntry
        {
            // This field has to fit in HashMask (13 bits)
            public int Hash             // the hash associated with Value
            {
                get { return (HashAge & HashMask); }
                set
                {
#if DEBUG
                    Debug.Assert((value & ~HashMask) == 0);
                    var beforeAge = Age;
#endif
                    HashAge = (ushort)(value | (HashAge & ~HashMask));
#if DEBUG
                    Debug.Assert(beforeAge == Age);
                    Debug.Assert(value == Hash);
#endif
                }
            }
            // This field has to fit in AgeMask (3 bits)
            public int Age              // a number that represents the last access (see GetFreeEntry)
            {
                get { return (int)(((uint)HashAge) >> HashBits); }
                set
                {
#if DEBUG
                    Debug.Assert((value & ~AgeMask) == 0);
                    var beforeHash = Hash;
#endif
                    HashAge = (ushort)((value << HashBits) | (HashAge & HashMask));

#if DEBUG
                    Debug.Assert(beforeHash == Hash);
                    Debug.Assert(value == Age);
#endif
                }
            }

            public K Key;
            public T Value;

            // We only have 32 bits of overhead over and above the key and value pair.  
            public ushort Next;        // index to the next entry in m_entries in the chain 
            #region private 
            private ushort HashAge;     // This field holds two bitfields.   
            #endregion 
        }

#if false
        public override string ToString()
        {
            System.IO.StringWriter writer = new System.IO.StringWriter();

            writer.WriteLine("Cache {");
            writer.WriteLine(" MaxEntries: {0}", MaxEntries);
            writer.WriteLine(" CurAge: {0}", m_curAge);
            writer.WriteLine(" EntriesInCurrentAge: {0}", m_entriesInCurAge);
            writer.WriteLine(" FreeScan: {0}", m_freeScan);
            writer.WriteLine(" FreeEntries: {0}", m_freeEntries);
            writer.WriteLine(" values: [");
            for (int i = 0; i < m_hashTable.Length; i++)
            {
                var entryIdx = m_hashTable[i];
                while (entryIdx != End)
                {
                    writer.WriteLine("   Entry {");
                    writer.WriteLine("      HashIdx: {0}", i);
                    writer.WriteLine("      EntryIdx: {0}", entryIdx);
                    writer.WriteLine("      Age: {0}", m_entries[entryIdx].Age);
                    writer.WriteLine("      Hash: {0}", m_entries[entryIdx].Hash);
                    writer.WriteLine("      Key: {0}", m_entries[entryIdx].Key.ToString());
                    writer.WriteLine("      Value: {0}", m_entries[entryIdx].Value.ToString());
                    writer.WriteLine("   },");
                    entryIdx = m_entries[entryIdx].Next;
                }
            }

            writer.WriteLine(" ],");
            writer.WriteLine("}");
            return writer.ToString();
        }
#endif

        /// <summary>
        /// Represents a null pointer (end of a linked list)
        /// </summary>
        private const ushort End = 0xFFFF;
        private const int HashBits = 13;           // 13 bits for the hash
        private const int HashMask = (1 << HashBits) - 1;
        private const int AgeMask = 0x7;            // 3 bits for the age

        // fields...
        private ushort[] m_hashTable;         // We hash here, which returns an index 
        private CacheEntry[] m_entries;       // which point here.  Effectively this is open hashing, but is more efficient than using pointers.  
        private ushort m_freeEntries;         // from 0 to m_freeEntries-1 are free. 
                                              // when we run out of free entries, we look for old ones.  Remembers the m_hashTable entry were we are in this scan.

        private int m_freeScan;

        // Every time a entry is accessed, it is marked with the current age.   
        // Every time we have marked 1/2 the entries this way, we move on to a new age.
        // Thus we want to reclaim entries that are 'far away' from this current age.   See GetFreeEntry() for more.  
        private byte m_curAge;                // The age that represents 'now' 
        private ushort m_entriesInCurAge;     // The number of entries that have been marked as being used 'now'
        #endregion
    }
}
