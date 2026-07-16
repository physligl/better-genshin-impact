using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoFight.Model;
using BetterGenshinImpact.GameTask.AutoPathing;
using BetterGenshinImpact.GameTask.AutoPathing.Domain;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.AutoPathing;

public class PathingHealthControllerTests
{
    [Fact]
    public async Task CheckAndAttemptRecoveryAsync_WhenRecoveryIsDisabled_DoesNotInspectOrRecover()
    {
        var vision = new RecordingVisionService();
        var teleport = new RecordingTeleportService();
        var controller = CreateController(vision, teleport);
        var config = new PathingPartyConfig { RecoverTiming = RecoverTiming.Never };
        var waypoint = CreateWaypoint(WaypointType.Path.Code);

        var result = await controller.CheckAndAttemptRecoveryAsync(waypoint, null, config, CancellationToken.None);

        Assert.Equal(HealthRecoveryResult.HealthyAndContinue, result);
        Assert.Equal(0, vision.InspectionCount);
        Assert.Equal(0, teleport.TeleportCount);
    }

    [Fact]
    public async Task CheckAndAttemptRecoveryAsync_WhenRecoveryIsTeleportOnly_SkipsOrdinaryWaypoint()
    {
        var vision = new RecordingVisionService();
        var teleport = new RecordingTeleportService();
        var controller = CreateController(vision, teleport);
        var config = new PathingPartyConfig { RecoverTiming = RecoverTiming.OnlyTeleport };
        var waypoint = CreateWaypoint(WaypointType.Path.Code);

        var result = await controller.CheckAndAttemptRecoveryAsync(waypoint, null, config, CancellationToken.None);

        Assert.Equal(HealthRecoveryResult.HealthyAndContinue, result);
        Assert.Equal(0, vision.InspectionCount);
        Assert.Equal(0, teleport.TeleportCount);
    }

    private static PathingHealthController CreateController(
        RecordingVisionService vision,
        RecordingTeleportService teleport)
    {
        return new PathingHealthController(
            NullLogger<PathingHealthController>.Instance,
            vision,
            new StubPartyService(),
            new StubInputService(),
            teleport,
            []);
    }

    private static WaypointForTrack CreateWaypoint(string type)
    {
        return new WaypointForTrack(
            new Waypoint { X = 0, Y = 0, Type = type },
            "Teyvat",
            "TemplateMatch");
    }

    private sealed class RecordingVisionService : IVisionService
    {
        public int InspectionCount { get; private set; }

        public bool ClickIfInReviveModal()
        {
            InspectionCount++;
            return false;
        }

        public bool IsCurrentAvatarLowHp()
        {
            InspectionCount++;
            return false;
        }

        public Task WaitForMainUiAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class RecordingTeleportService : ITeleportService
    {
        public int TeleportCount { get; private set; }

        public Task TeleportToStatueOfTheSevenAsync(CancellationToken ct)
        {
            TeleportCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class StubPartyService : IPartyService
    {
        public Task<Avatar?> SwitchToAvatarAsync(
            string avatarId,
            bool isInstant = false,
            CancellationToken ct = default)
        {
            return Task.FromResult<Avatar?>(null);
        }
    }

    private sealed class StubInputService : IInputService
    {
        public void ExecuteAction(GIActions action)
        {
        }
    }
}
