using System;
using System.Collections.Generic;
using System.Linq;

namespace DestroyChecker.Core.Models
{
    /// <summary>
    /// Input data representing a single achievement (collection) with its bits and account progress.
    /// Frontends (Console, Blish) should map their API responses into this DTO before calling CollectionMapper.
    /// </summary>
    public class AchievementData
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public List<AchievementBit> Bits { get; set; } = new List<AchievementBit>();
    }

    public class AchievementBit
    {
        public string Type { get; set; } = string.Empty;
        public int? Id { get; set; }
    }

    public class AccountAchievementProgress
    {
        public int Id { get; set; }
        public bool Done { get; set; }
        public int Current { get; set; }
        public int Max { get; set; }
    }
}
