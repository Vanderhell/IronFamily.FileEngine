using System;
using System.Collections.Generic;

namespace MegaBench.Profiles
{
    public enum EngineKind
    {
        ICFG = 0,
        ILOG = 1,
        IUPD = 2
    }

    public static class ProfileMatrix
    {
        public static readonly (EngineKind Engine, string Profile, string SizeLabel, string DatasetId)[] Items = new[]
        {
            // ICFG: 1 profile (DEFAULT)
            (EngineKind.ICFG, "DEFAULT", "1KB", "ironcfg_1KB"),
            (EngineKind.ICFG, "DEFAULT", "10KB", "ironcfg_10KB"),
            (EngineKind.ICFG, "DEFAULT", "1MB", "ironcfg_1MB"),

            // ILOG: 5 profiles
            (EngineKind.ILOG, "MINIMAL", "10KB", "ilog_MINIMAL_10KB"),
            (EngineKind.ILOG, "INTEGRITY", "10KB", "ilog_INTEGRITY_10KB"),
            (EngineKind.ILOG, "SEARCHABLE", "10KB", "ilog_SEARCHABLE_10KB"),
            (EngineKind.ILOG, "ARCHIVED", "10KB", "ilog_ARCHIVED_10KB"),
            (EngineKind.ILOG, "AUDITED", "10KB", "ilog_AUDITED_10KB"),

            // IUPD: 4 profiles
            (EngineKind.IUPD, "MINIMAL", "10KB", "iupd_MINIMAL_10KB"),
            (EngineKind.IUPD, "FAST", "10KB", "iupd_FAST_10KB"),
            (EngineKind.IUPD, "SECURE", "10KB", "iupd_SECURE_10KB"),
            (EngineKind.IUPD, "OPTIMIZED", "10KB", "iupd_OPTIMIZED_10KB"),
        };

        /// <summary>
        /// Lookup engine and profile by datasetId.
        /// </summary>
        public static (EngineKind Engine, string Profile, string SizeLabel) LookupByDatasetId(string datasetId)
        {
            foreach (var item in Items)
            {
                if (item.DatasetId == datasetId)
                {
                    return (item.Engine, item.Profile, item.SizeLabel);
                }
            }
            throw new InvalidOperationException($"UNKNOWN_DATASETID_NO_PROFILE_MAPPING: {datasetId}");
        }
    }
}
