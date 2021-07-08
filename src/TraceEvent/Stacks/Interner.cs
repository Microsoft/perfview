using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Microsoft.Diagnostics.Tracing.Stacks
{
    public class Interner
    {
        private GrowableArray<string> _strings;
        private readonly Dictionary<string, int> _stringToIndex;
        private StrongBox<Interner> _version;

        public Interner()
        {
            _stringToIndex = new Dictionary<string, int>();
            _version = new StrongBox<Interner>(this);
        }

        public Key Intern(string value)
        {
            int index;
            if (_stringToIndex.TryGetValue(value, out index))
            {
                return new Key(this, _strings[index], index);
            }

            index = _strings.Count;
            _stringToIndex[value] = index;
            _strings.Set(index, value);
            return new Key(this, value, index);
        }

        private void Clear()
        {
            _strings.Count = 0;
            _stringToIndex.Clear();
            _version.Value = null;
            _version = new StrongBox<Interner>(this);
        }

        public struct Key
        {
            private readonly StrongBox<Interner> _internerId;
            private readonly string _value;
            private readonly int _index;

            public Key(Interner interner, string value, int index)
            {
                Debug.Assert(index == -1 || ReferenceEquals(interner._strings[index], value));
                _internerId = interner._version;
                _value = value;
                _index = index;
            }

            public int Index
            {
                get
                {
                    Debug.Assert(_index >= 0 && _internerId.Value != null);
                    return _index;
                }
            }

            public override string ToString()
            {
                return _value;
            }
        }
    }
}
