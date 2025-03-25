using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoGeniusInvokation.Exception;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoWood.Assets;
using BetterGenshinImpact.GameTask.AutoWood.Utils;
using BetterGenshinImpact.GameTask.Common;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using BetterGenshinImpact.GameTask.Common.Job;
using BetterGenshinImpact.GameTask.Model.Area;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static BetterGenshinImpact.GameTask.Common.TaskControl;

namespace BetterGenshinImpact.GameTask.AutoPathing.Handler;

public class ExitAndReloginHandler: IActionHandler
{
    private AutoWoodAssets _assets;
    private readonly Login3rdParty _login3rdParty = new();
    public async Task RunAsync(CancellationToken ct, WaypointForTrack? waypointForTrack = null, object? config = null)
    {
        //============== 退出游戏流程 ==============
        _assets = AutoWoodAssets.Instance;
        SystemControl.FocusWindow(TaskContext.Instance().GameHandle);
        Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
        await Delay(800, ct);

        // 菜单界面验证（带重试机制）
        try
        {
            NewRetry.Do(() => 
            {
                using var contentRegion = CaptureToRectArea();
                using var ra = contentRegion.Find(_assets.MenuBagRo);
                if (ra.IsEmpty())
                {
                    // 未检测到菜单时再次发送ESC
                    Simulation.SendInput.Keyboard.KeyPress(User32.VK.VK_ESCAPE);
                    throw new RetryException("菜单界面验证失败");
                }
            }, TimeSpan.FromSeconds(1.2), 5);  // 1.2秒内重试5次
        }
        catch
        {
            // 即使失败也继续退出流程
        }

        // 点击退出按钮
        GameCaptureRegion.GameRegionClick((size, scale) => (50 * scale, size.Height - 50 * scale));
        await Delay(500, ct);

        // 确认退出
        using (var confirmRegion = CaptureToRectArea())
        {
            confirmRegion.Find(_assets.ConfirmRo, ra => ra.Click());
        }
        await Delay(1000, ct);  // 等待退出完成

        //============== 重新登录流程 ==============
        // 第三方登录（如果启用）
        _login3rdParty.RefreshAvailabled();
        if (_login3rdParty.Type == Login3rdParty.The3rdPartyType.Bilibili)
        {
            Logger.LogInformation("退出重登启用 B 服模式");
        }

        // 进入游戏检测
        int entryAttempts = 0;
        for (int i = 0; i < 50; i++)  // 总尝试时间约50秒
        {
            using var contentRegion = CaptureToRectArea();
            using var ra = contentRegion.Find(_assets.EnterGameRo);
        
            if (!ra.IsEmpty())
            {
                // 点击进入游戏按钮
                GameCaptureRegion.GameRegion1080PPosClick(955, 666);
                entryAttempts++;
            
                // 成功点击3次后认为有效
                if (entryAttempts >= 3) break;  
            }
            await Delay(1000, ct);
        }

        // 登录结果验证
        if (entryAttempts < 1)
        {
            throw new Exception("重新登录失败：未检测到进入游戏按钮");
        }

        for (var i = 0; i < 50; i++)
        {
            if (Bv.IsInMainUi(CaptureToRectArea()))
            {
                Logger.LogInformation("动作：退出重新登录结束！");
                break;
            }
            await Delay(1000, ct);
        }
        await Delay(500, ct);
    }
}