using UnityEngine;

namespace LogSyncMod.AutoTest
{
    internal sealed class AutoTestScreenshotProfile
    {
        public string ProfileId;
        public float RangeScale;
        public float MinRootSize;
        public float MaxRootSize;
        public float CellMargin;
        public float CameraPlusMinRootSize;
        public float CameraPlusMaxRootSize;
        public float CameraPlusCellMargin;
        public float CameraHeight = 65f;
        public float OverviewScale = 0.58f;
        public float? FixedRootSize;
    }

    internal sealed class AutoTestScreenshotRequest
    {
        public AutoTestResult Result;
        public bool AllowCapture;
        public bool Force;
        public bool IsFailureArtifact;
        public int ResultIndex;
        public string StageSuffix;
        public string RelativeSubdirectory;
        public string SuccessNoteKey;
        public string FailureNoteKey;
        public AutoTestScreenshotProfile Profile;
        public System.Action Prepare;
        public System.Func<bool> Guard;
        public System.Func<AutoTestCaptureProbeResult> CaptureProbe;
        public bool RequireCaptureProbePass;
    }

    internal sealed class AutoTestCaptureProbeResult
    {
        public bool Ready;
        public string Details;
    }

    internal static class AutoTestScreenshotProfiles
    {
        public static readonly AutoTestScreenshotProfile InfoCard = new AutoTestScreenshotProfile
        {
            ProfileId = "InfoCard"
        };

        public static readonly AutoTestScreenshotProfile TurretPrimary = new AutoTestScreenshotProfile
        {
            ProfileId = "TurretPrimary",
            RangeScale = 3.81248f,
            MinRootSize = 11f,
            MaxRootSize = 26f,
            CellMargin = 3f,
            CameraPlusMinRootSize = 11f,
            CameraPlusMaxRootSize = 21f,
            CameraPlusCellMargin = 1.5f
        };

        public static readonly AutoTestScreenshotProfile RangedPrimary = new AutoTestScreenshotProfile
        {
            ProfileId = "RangedPrimary",
            RangeScale = 3.15f,
            MinRootSize = 13f,
            MaxRootSize = 26f,
            CellMargin = 3f,
            CameraPlusMinRootSize = 13f,
            CameraPlusMaxRootSize = 21f,
            CameraPlusCellMargin = 1.5f
        };

        public static readonly AutoTestScreenshotProfile MeleePrimary = new AutoTestScreenshotProfile
        {
            ProfileId = "MeleePrimary",
            FixedRootSize = 11f,
            MinRootSize = 11f,
            MaxRootSize = 11f
        };

        public static readonly AutoTestScreenshotProfile MeleeAction = new AutoTestScreenshotProfile
        {
            ProfileId = "MeleeAction",
            FixedRootSize = 11f,
            MinRootSize = 11f,
            MaxRootSize = 11f
        };

        public static readonly AutoTestScreenshotProfile PocketMapOverview = new AutoTestScreenshotProfile
        {
            ProfileId = "PocketMapOverview",
            MinRootSize = 26f,
            MaxRootSize = 26f,
            OverviewScale = 0.58f
        };

        public static readonly AutoTestScreenshotProfile ThreeViewHumanoid = new AutoTestScreenshotProfile
        {
            ProfileId = "ThreeViewHumanoid",
            FixedRootSize = 11f,
            MinRootSize = 11f,
            MaxRootSize = 11f
        };

        public static readonly AutoTestScreenshotProfile ThreeViewAnimal = new AutoTestScreenshotProfile
        {
            ProfileId = "ThreeViewAnimal",
            FixedRootSize = 11f,
            MinRootSize = 11f,
            MaxRootSize = 11f,
            CameraHeight = 60f
        };

        public static readonly AutoTestScreenshotProfile ThreeViewMechanoid = new AutoTestScreenshotProfile
        {
            ProfileId = "ThreeViewMechanoid",
            FixedRootSize = 11f,
            MinRootSize = 11f,
            MaxRootSize = 11f,
            CameraHeight = 60f
        };

        public static readonly AutoTestScreenshotProfile ThreeViewAlien = new AutoTestScreenshotProfile
        {
            ProfileId = "ThreeViewAlien",
            FixedRootSize = 11f,
            MinRootSize = 11f,
            MaxRootSize = 11f
        };
    }
}
