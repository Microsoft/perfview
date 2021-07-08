using System;

namespace Microsoft.Diagnostics.Tracing.Stacks
{
    public static class RecursionGuardConfiguration
    {
        private static ushort _maxResets = ushort.MaxValue;

        /// <summary>
        /// The number of times to trampoline to a new thread before assuming infinite recursion and failing the operation.
        /// </summary>
        public static ushort MaxResets
        {
            get { return _maxResets; }
            set { _maxResets = value; }
        }
    }

    /// <summary>
    /// This structure provides a clean API for a lightweight recursion stack guard to prevent StackOverflow exceptions
    /// We do ultimately do a stack-overflow to prevent infinite recursion, but it is now under our
    /// control and much larger than you may get on any one thread stack.  
    /// </summary>
    internal struct RecursionGuard
    {
        private readonly ushort _currentThreadRecursionDepth;
        private readonly ushort _resetCount;

        /// <summary>
        /// For recursive methods that need to process deep stacks, this constant defines the limit for recursion within
        /// a single thread. After reaching this limit, methods need to trampoline to a new thread before continuing to
        /// recurse.
        /// </summary>
        internal const ushort SingleThreadRecursionLimit = 400;

        private RecursionGuard(int currentThreadRecursionDepth, int numResets = 0)
        {
            if (numResets > RecursionGuardConfiguration.MaxResets)
            {
#if NETSTANDARD1_6
                throw new Exception("Stack Overflow");
#else 
                throw new StackOverflowException();
#endif
            }
            _currentThreadRecursionDepth = (ushort)currentThreadRecursionDepth;
            _resetCount = (ushort)numResets;
        }

        /// <summary>
        /// The amount of recursion we have currently done.  
        /// </summary>
        public int Depth => (_resetCount * SingleThreadRecursionLimit) + _currentThreadRecursionDepth;

        /// <summary>
        /// Gets the recursion guard for entering a recursive method.
        /// </summary>
        /// <remarks>
        /// This is equivalent to the default <see cref="RecursionGuard"/> value.
        /// </remarks>
        public static RecursionGuard Entry => default(RecursionGuard);

        /// <summary>
        /// Gets an updated recursion guard for recursing into a method.
        /// </summary>
        public RecursionGuard Recurse => new RecursionGuard(_currentThreadRecursionDepth + 1, _resetCount);

        /// <summary>
        /// Gets an updated recursion guard for continuing execution on a new thread.
        /// </summary>
        public RecursionGuard ResetOnNewThread => new RecursionGuard(0, _resetCount + 1);

        /// <summary>
        /// Gets a value indicating whether the current operation has exceeded the recursion depth for a single thread,
        /// and needs to continue executing on a new thread.
        /// </summary>
        public bool RequiresNewThread => _currentThreadRecursionDepth >= SingleThreadRecursionLimit;
    }
}
