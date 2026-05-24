namespace eft_dma_radar.Arena.Unity.Collections
{
    public sealed class MemArray<T> : SharedArray<T>, IPooledObject<MemArray<T>>
        where T : unmanaged
    {
        public const uint CountOffset  = 0x18;
        public const uint ArrBaseOffset = 0x20;

        public static MemArray<T> Get(ulong addr, bool useCache = true)
        {
            var arr = IPooledObject<MemArray<T>>.Rent();
            arr.Initialize(addr, useCache);
            return arr;
        }

        public static MemArray<T> Get(ulong addr, int count, bool useCache = true)
        {
            var arr = IPooledObject<MemArray<T>>.Rent();
            arr.Initialize(addr, count, useCache);
            return arr;
        }

        /// <summary>
        /// Non-throwing factory. Returns <c>true</c> + a rented array on success;
        /// on any failure (bad pointer, bogus count, partial read) returns false
        /// and <paramref name="arr"/> is null with no allocation leaked.
        /// <para>
        /// Use this instead of <see cref="Get(ulong,bool)"/> in hot loops that
        /// probe many candidate addresses — the throwing variant fires a
        /// first-chance exception per failed candidate, and the Arena match log
        /// shows that single misuse site (the inventory-controller-offset
        /// probe) generating hundreds of <c>VmmException</c> /
        /// <c>ArgumentOutOfRangeException</c> events per match. Each exception
        /// burns ~10 µs of stack walking — switching to this path eliminates
        /// the entire cost.
        /// </para>
        /// </summary>
        public static bool TryGet(ulong addr, out MemArray<T>? arr, bool useCache = true)
        {
            arr = IPooledObject<MemArray<T>>.Rent();
            if (arr.TryInitialize(addr, useCache))
                return true;
            arr.Dispose();
            arr = null;
            return false;
        }

        private void Initialize(ulong addr, bool useCache = true)
        {
            try
            {
                var count = Memory.ReadValue<int>(addr + CountOffset, useCache);
                ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 16384, nameof(count));
                Initialize(count);
                if (count == 0) return;
                Memory.ReadBuffer(addr + ArrBaseOffset, Span, useCache);
            }
            catch { Dispose(); throw; }
        }

        /// <summary>
        /// Non-throwing initializer used by <see cref="TryGet"/>. Same shape as
        /// <see cref="Initialize(ulong,bool)"/> but each step is gated through
        /// the <c>TryRead*</c> API and bogus counts return false instead of
        /// throwing <see cref="ArgumentOutOfRangeException"/>.
        /// </summary>
        private bool TryInitialize(ulong addr, bool useCache)
        {
            if (!Memory.TryReadValue<int>(addr + CountOffset, out int count, useCache))
                return false;
            if ((uint)count > 16384) return false; // negatives + bogus huge counts
            Initialize(count);
            if (count == 0) return true;
            return Memory.TryReadBuffer(addr + ArrBaseOffset, Span, useCache);
        }

        private void Initialize(ulong addr, int count, bool useCache = true)
        {
            try
            {
                Initialize(count);
                if (count == 0) return;
                Memory.ReadBuffer(addr, Span, useCache);
            }
            catch { Dispose(); throw; }
        }

        [Obsolete("You must rent this object via IPooledObject!")]
        public MemArray() : base() { }

        protected override void Dispose(bool disposing)
        {
            IPooledObject<MemArray<T>>.Return(this);
        }
    }
}
