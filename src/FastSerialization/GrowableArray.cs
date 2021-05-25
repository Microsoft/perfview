using System.Diagnostics;
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// This file is best viewed using outline mode (Ctrl-M Ctrl-O)
//
// This program uses code hyperlinks available as part of the HyperAddin Visual Studio plug-in.
// It is available from http://www.codeplex.com/hyperAddin 
// 
using System.Text;

namespace System.Collections.Generic
{
    /// <summary>
    /// A cheap version of List(T). The idea is to make it as cheap as if you did it 'by hand' using an array and
    /// an int which represents the logical charCount. It is a struct to avoid an extra pointer dereference, so this
    /// is really meant to be embedded in other structures.
    /// </summary>
    [DebuggerDisplay("Count = {Count}")]
#if GROWABLEARRAY_PUBLIC
    public
#endif
    struct GrowableArray<T>
    {
        /// <summary>
        /// Create a growable array with the given initial size it will grow as needed.  There is also the
        /// default constructor that assumes initialSize of 0 (and does not actually allocate the array. 
        /// </summary>
        /// <param name="initialSize"></param>
        public GrowableArray(int initialSize)
        {
            array = new T[initialSize];
            arrayLength = 0;
        }
        /// <summary>
        /// Fetch the element at the given index.  Will throw an IndexOutOfRange exception otherwise
        /// </summary>
        public T this[int index]
        {
            get
            {
                Debug.Assert((uint)index < (uint)arrayLength);
                return array[index];
            }
            set
            {
                Debug.Assert((uint)index < (uint)arrayLength);
                array[index] = value;
            }
        }
        /// <summary>
        /// The number of elements in the array
        /// </summary>
        public int Count
        {
            get
            {
                return arrayLength;
            }
            set
            {
                if (arrayLength < value)
                {
                    if (array != null && value <= array.Length)
                    {
                        // Null out the entries.  
                        for (int i = arrayLength; i < value; i++)
                        {
                            array[i] = default(T);
                        }
                    }
                    else
                    {
                        Realloc(value);
                    }
                }
                arrayLength = value;
            }
        }
        /// <summary>
        /// Remove all elements in the array. 
        /// </summary>
        public void Clear()
        {
            arrayLength = 0;
        }

        /// <summary>
        /// Add an item at the end of the array, growing as necessary. 
        /// </summary>
        /// <param name="item"></param>
        public void Add(T item)
        {
            if (array == null || arrayLength >= array.Length)
            {
                Realloc(0);
            }

            array[arrayLength++] = item;
        }
        /// <summary>
        /// Add all items 'items' to the end of the array, growing as necessary. 
        /// </summary>
        /// <param name="items"></param>
        public void AddRange(IEnumerable<T> items)
        {
            foreach (T item in items)
            {
                Add(item);
            }
        }
        /// <summary>
        /// Insert 'item' directly at 'index', shifting all items >= index up.  'index' can be code:Count in
        /// which case the item is appended to the end.  Larger indexes are not allowed. 
        /// </summary>
        public void Insert(int index, T item)
        {
            if ((uint)index > (uint)arrayLength)
            {
                throw new IndexOutOfRangeException();
            }

            if (array == null || arrayLength >= array.Length)
            {
                Realloc(0);
            }

            // Shift everything up to make room. 
            for (int idx = arrayLength; index < idx; --idx)
            {
                array[idx] = array[idx - 1];
            }

            // insert the element
            array[index] = item;
            arrayLength++;
        }
        /// <summary>
        /// Remove 'count' elements starting at 'index'
        /// </summary>
        public void RemoveRange(int index, int count)
        {
            if (count == 0)
            {
                return;
            }

            if (count < 0)
            {
                throw new ArgumentException("count can't be negative");
            }

            if ((uint)index >= (uint)arrayLength)
            {
                throw new IndexOutOfRangeException();
            }

            Debug.Assert(index + count <= arrayLength);     // If you violate this it does not hurt

            // Shift everything down. 
            for (int endIndex = index + count; endIndex < arrayLength; endIndex++)
            {
                array[index++] = array[endIndex];
            }

            arrayLength = index;
        }

        // Support for using an array as a map from int to T.    
        /// <summary>
        /// Sets the 'index' element to 'value' growing the array if necessary (filling in default values if necessary).  
        /// </summary>
        public void Set(int index, T value)
        {
            GetRef(index) = value;
        }

        public ref T GetRef(int index)
        {
            if (index >= Count)
                Count = index + 1;
            return ref array[index];
        }
        /// <summary>
        /// Gets the value at 'index'.   Never fails, will return 'default' if out of range.  
        /// </summary>
        public T Get(int index)
        {
            if ((uint)index < (uint)arrayLength)
            {
                return this[index];
            }
            else
            {
                return default(T);
            }
        }

        // Support for stack-like operations 
        /// <summary>
        /// Returns true if there are no elements in the array. 
        /// </summary>
        public bool Empty { get { return arrayLength == 0; } }
        /// <summary>
        /// Remove the last element added and return it. Will throw if there are no elements.
        /// </summary>
        /// <returns></returns>
        public T Pop()
        {
            T ret = array[arrayLength - 1];       // Will cause index out of range exception
            --arrayLength;
            return ret;
        }
        /// <summary>
        /// Returns the last element added  Will throw if there are no elements. 
        /// </summary>
        public T Top { get { return array[arrayLength - 1]; } }

        /// <summary>
        /// Trims the size of the array so that no more than 'maxWaste' slots are wasted.   Useful when
        /// you know that the array has stopped growing.  
        /// </summary>
        public void Trim(int maxWaste)
        {
            if (array != null)
            {
                if (array.Length > arrayLength + maxWaste)
                {
                    if (arrayLength == 0)
                    {
                        array = null;
                    }
                    else
                    {
                        T[] newArray = new T[arrayLength];
                        Array.Copy(array, newArray, arrayLength);
                        array = newArray;
                    }
                }
            }
        }

        /// <summary>
        /// Returns true if the Growable array was initialized by the default constructor
        /// which has no capacity (and thus will cause growth on the first addition).
        /// This method allows you to lazily set the compacity of your GrowableArray by
        /// testing if it is of EmtpyCapacity, and if so set it to some useful capacity.
        /// This avoids unnecessary reallocs to get to a reasonable capacity.   
        /// </summary>
        public bool EmptyCapacity { get { return array == null; } }

        /// <summary>
        /// A string representing the array.   Only intended for debugging.  
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("GrowableArray(Count=").Append(Count).Append(", [").AppendLine();
            for (int i = 0; i < Count; i++)
            {
                sb.Append("  ").Append(this[i].ToString()).AppendLine();
            }

            sb.Append("  ])");
            return sb.ToString();
        }

        /// <summary>
        /// Sets 'index' to the the smallest index such that all elements with index > 'idx' are > key.  If
        /// index does not match any elements a new element should always be placed AFTER index.  Note that this
        /// means that index may be -1 if the new element belongs in the first position.  
        /// 
        /// Returns true if the return index matched exactly (success)
        /// 
        /// TODO FIX NOW harmonize with List.BinarySearch
        /// </summary>
        public bool BinarySearch<Key>(Key key, out int index, Func<Key, T, int> comparison)
        {
            // binary search 
            int low = 0;
            int high = arrayLength;
            int lastLowCompare = -1;                // If this number == 0 we had a match. 

            if (high > 0)
            {
                // The invariant in this loop is that 
                //     [0..low) <= key < [high..Count)
                for (; ; )
                {
                    int mid = (low + high) / 2;
                    int compareResult = comparison(key, array[mid]);
                    if (compareResult >= 0)             // key >= array[mid], move low up
                    {
                        lastLowCompare = compareResult; // remember this result, as it indicates a successful match. 
                        if (mid == low)
                        {
                            break;
                        }

                        low = mid;
                    }
                    else                                // key < array[mid], move high down 
                    {
                        high = mid;
                        if (mid == low)
                        {
                            break;
                        }
                    }

                    // Note that if compareResults == 0, we don't return the match eagerly because there could be
                    // multiple elements that match. We want the match with the largest possible index, so we need
                    // to continue the search until the valid range drops to 0
                }
            }

            if (lastLowCompare < 0)            // key < array[low], subtract 1 to indicate that new element goes BEFORE low. 
            {
                Debug.Assert(low == 0);         // can only happen if it is the first element
                --low;
            }
            index = low;

            Debug.Assert(index == -1 || comparison(key, array[index]) >= 0);                 // element smaller or equal to key            
            Debug.Assert(index + 1 >= Count || comparison(key, array[index + 1]) < 0);       // The next element is strictly bigger.
            Debug.Assert((lastLowCompare != 0) || (comparison(key, array[index]) == 0));     // If we say there is a match, there is. 
            return (lastLowCompare == 0);
        }
        /// <summary>
        /// Sort the range starting at 'index' of length 'count' using 'comparision' in assending order
        /// </summary>
        public void Sort(int index, int count, Comparison<T> comparison)
        {
            Debug.Assert(index + count <= arrayLength);
            if (count > 0)
            {
                Array.Sort<T>(array, index, count, new FunctorComparer<T>(comparison));
            }
        }
        /// <summary>
        /// Sort the whole array using 'comparison' in ascending order
        /// </summary>
        public void Sort(Comparison<T> comparison)
        {
            if (array != null)
            {
                Array.Sort<T>(array, 0, arrayLength, new FunctorComparer<T>(comparison));
            }
        }

        /// <summary>
        /// Executes 'func' for each element in the GrowableArray and returns a GrowableArray 
        /// for the result.  
        /// </summary>
        public GrowableArray<T1> Foreach<T1>(Func<T, T1> func)
        {
            var ret = new GrowableArray<T1>();
            ret.Count = Count;

            for (int i = 0; i < Count; i++)
            {
                ret[i] = func(array[i]);
            }

            return ret;
        }

        /// <summary>
        /// Perform a linear search starting at 'startIndex'.  If found return true and the index in 'index'.
        /// It is legal that 'startIndex' is greater than the charCount, in which case, the search returns false
        /// immediately.   This allows a nice loop to find all items matching a pattern. 
        /// </summary>
        public bool Search<Key>(Key key, int startIndex, Func<Key, T, int> compare, ref int index)
        {
            for (int i = startIndex; i < arrayLength; i++)
            {
                if (compare(key, array[i]) == 0)
                {
                    index = i;
                    return true;
                }
            }
            return false;
        }
        /// <summary>
        /// Returns the underlying array.  Should not be used most of the time!
        /// </summary>
        public T[] UnderlyingArray { get { return array; } }
        #region private
        private void Realloc(int minSize)
        {
            if (array == null)
            {
                if (minSize < 16)
                {
                    minSize = 16;
                }

                array = new T[minSize];
            }
            else
            {
                long expandSize = (long)array.Length * 3 / 2 + 8;
                if (expandSize > int.MaxValue)
                {
                    if (array.Length == int.MaxValue)
                        throw new NotSupportedException("Array cannot have more than int.MaxValue elements.");

                    expandSize = int.MaxValue;
                }

                if (minSize < expandSize)
                {
                    minSize = (int)expandSize;
                }

                Array.Resize(ref array, minSize);
            }
        }

        private T[] array;
        private int arrayLength;
        #endregion

        #region TESTING
        // Unit testing.  It is reasonable coverage, but concentrates on BinarySearch as that is the one that is
        // easy to get wrong.  
#if TESTING
   public static void TestGrowableArray()
    {
        GrowableArray<float> testArray = new GrowableArray<float>();
        for (float i = 1.1F; i < 10; i += 2)
        {
            int successes = TestBinarySearch(testArray);
            Debug.Assert(successes == ((int)i) / 2);
            testArray.Add(i);
        }

        for (float i = 0.1F; i < 11; i += 2)
        {
            int index;
            bool result = testArray.BinarySearch(i, out index, delegate(float key, float elem) { return (int)key - (int)elem; });
            Debug.Assert(!result);
            testArray.InsertAt(index + 1, i);
        }

        int lastSuccesses = TestBinarySearch(testArray);
        Debug.Assert(lastSuccesses == 11);

        for (float i = 0; i < 11; i += 1)
        {
            int index;
            bool result = testArray.BinarySearch(i, out index, delegate(float key, float elem) { return (int)key - (int)elem; });
            Debug.Assert(result);
            testArray.InsertAt(index + 1, i);
        }

        lastSuccesses = TestBinarySearch(testArray);
        Debug.Assert(lastSuccesses == 11);

        // We always get the last one when the equality comparison allows multiple items to match.  
        for (float i = 0; i < 11; i += 1)
        {
            int index;
            bool result = testArray.BinarySearch(i, out index, delegate(float key, float elem) { return (int)key - (int)elem; });
            Debug.Assert(result);
            Debug.Assert(i == testArray[index]);
        }
        Console.WriteLine("Done");
    }
    private static int TestBinarySearch(GrowableArray<float> testArray)
    {
        int successes = 0;
        for (int i = 0; i < 30; i++)
        {
            int index;
            if (testArray.BinarySearch(i, out index, delegate(float key, float elem) { return (int)key - (int)elem; }))
            {
                successes++;
                Debug.Assert((int)testArray[index] == i);
            }
            else
                Debug.Assert(index + 1 <= testArray.Count);
        }
        return successes;
}
#endif
        #endregion

        // This allows 'foreach' to work.  We are not a true IEnumerable however.  
        /// <summary>
        /// Implementation of foreach protocol
        /// </summary>
        /// <returns></returns>
        public GrowableArrayEnumerator GetEnumerator() { return new GrowableArrayEnumerator(this); }
        /// <summary>
        /// Enumerator for foreach interface
        /// </summary>
        public struct GrowableArrayEnumerator
        {
            /// <summary>
            /// implementation of IEnumerable interface
            /// </summary>
            public T Current
            {
                get { return array[cur]; }
            }
            /// <summary>
            /// implementation of IEnumerable interface
            /// </summary>
            public bool MoveNext()
            {
                cur++;
                return cur < end;
            }

            #region private
            internal GrowableArrayEnumerator(GrowableArray<T> growableArray)
            {
                cur = -1;
                end = growableArray.arrayLength;
                array = growableArray.array;
            }

            private int cur;
            private int end;
            private T[] array;
            #endregion
        }
    }
}
