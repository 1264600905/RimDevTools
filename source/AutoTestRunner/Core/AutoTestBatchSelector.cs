using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace LogSyncMod.AutoTest
{
    internal static class AutoTestBatchSelector
    {
        public static IReadOnlyList<int> SelectIndices(int candidateCount, int limit)
        {
            if (candidateCount < 0) throw new ArgumentOutOfRangeException(nameof(candidateCount));
            if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));

            int selectedCount = Math.Min(candidateCount, limit);
            int[] indices = new int[candidateCount];
            for (int index = 0; index < candidateCount; index++) indices[index] = index;

            if (selectedCount == candidateCount)
            {
                return indices;
            }

            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                for (int index = 0; index < selectedCount; index++)
                {
                    int selectedIndex = NextInt32(random, index, candidateCount);
                    int value = indices[index];
                    indices[index] = indices[selectedIndex];
                    indices[selectedIndex] = value;
                }
            }

            int[] selected = new int[selectedCount];
            Array.Copy(indices, selected, selectedCount);
            Array.Sort(selected);
            return selected;
        }

        private static int NextInt32(RandomNumberGenerator random, int minInclusive, int maxExclusive)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (minInclusive < 0 || minInclusive >= maxExclusive) throw new ArgumentOutOfRangeException(nameof(minInclusive));

            uint range = (uint)(maxExclusive - minInclusive);
            uint threshold = unchecked(0u - range) % range;
            byte[] buffer = new byte[4];
            uint value;
            do
            {
                random.GetBytes(buffer);
                value = BitConverter.ToUInt32(buffer, 0);
            }
            while (value < threshold);

            return minInclusive + (int)(value % range);
        }
    }
}
