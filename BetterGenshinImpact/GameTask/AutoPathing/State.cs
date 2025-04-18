using System;
using System.Threading;
using System.Threading.Tasks;
using BetterGenshinImpact.Core.Simulator;
using BetterGenshinImpact.Core.Simulator.Extensions;
using BetterGenshinImpact.GameTask.AutoPathing.Model;
using BetterGenshinImpact.GameTask.AutoPathing.Model.Enum;
using BetterGenshinImpact.GameTask.Common.BgiVision;
using Microsoft.Extensions.Logging;
using static BetterGenshinImpact.GameTask.Common.TaskControl;
using OpenCvSharp;

namespace BetterGenshinImpact.GameTask.AutoPathing
{
    /// <summary>
    /// 移动状态接口 - 定义所有移动状态的通用行为
    /// </summary>
    public interface IMovementState
    {
        /// <summary>
        /// 进入此状态时执行的操作
        /// </summary>
        Task EnterState();
        
        /// <summary>
        /// 在此状态下移动
        /// </summary>
        Task Move(WaypointForTrack waypoint, Point2f currentPosition, int targetOrientation);
        
        /// <summary>
        /// 退出此状态时执行的操作
        /// </summary>
        Task ExitState();
        
        /// <summary>
        /// 状态名称
        /// </summary>
        string Name { get; }
    }

    /// <summary>
    /// 移动状态管理上下文 - 负责状态切换和调用当前状态的方法
    /// </summary>
    public class MovementStateContext
    {
        private IMovementState _currentState;
        private readonly CancellationToken _ct;

        public MovementStateContext(CancellationToken cancellationToken)
        {
            _ct = cancellationToken;
            _currentState = new WalkingState(cancellationToken);
        }

        /// <summary>
        /// 切换到新状态
        /// </summary>
        public async Task TransitionTo(IMovementState newState)
        {
            Logger.LogInformation($"从 {_currentState.Name} 状态切换到 {newState.Name} 状态");
            await _currentState.ExitState();
            _currentState = newState;
            await _currentState.EnterState();
        }

        /// <summary>
        /// 根据移动模式自动切换状态
        /// </summary>
        public async Task SwitchStateByMoveMode(string moveMode)
        {
            IMovementState newState = moveMode switch
            {
                var mode when mode == MoveModeEnum.Walk.Code => 
                    new WalkingState(_ct),
                var mode when mode == MoveModeEnum.Run.Code => 
                    new RunningState(_ct),
                var mode when mode == MoveModeEnum.Fly.Code => 
                    new FlyingState(_ct),
                var mode when mode == MoveModeEnum.Climb.Code => 
                    new ClimbingState(_ct),
                var mode when mode == MoveModeEnum.Jump.Code => 
                    new JumpingState(_ct),
                var mode when mode == MoveModeEnum.Dash.Code => 
                    new DashingState(_ct),
                _ => new WalkingState(_ct)
            };

            if (_currentState.GetType() != newState.GetType())
            {
                await TransitionTo(newState);
            }
        }

        /// <summary>
        /// 在当前状态下移动
        /// </summary>
        public async Task Move(WaypointForTrack waypoint, Point2f currentPosition, int targetOrientation)
        {
            await _currentState.Move(waypoint, currentPosition, targetOrientation);
        }
    }

    /// <summary>
    /// 基础移动状态类 - 提供共享的功能
    /// </summary>
    public abstract class BaseMovementState : IMovementState
    {
        protected readonly CancellationToken CancellationToken;

        protected BaseMovementState(CancellationToken cancellationToken)
        {
            CancellationToken = cancellationToken;
        }

        public abstract string Name { get; }
        
        public virtual async Task EnterState()
        {
            Logger.LogDebug($"进入 {Name} 状态");
            await Task.CompletedTask;
        }

        public abstract Task Move(WaypointForTrack waypoint, Point2f currentPosition, int targetOrientation);

        public virtual async Task ExitState()
        {
            Logger.LogDebug($"退出 {Name} 状态");
            
            // 确保所有按键释放
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyUp);
            Simulation.SendInput.Mouse.RightButtonUp();
            
            await Task.CompletedTask;
        }

        protected async Task Delay(int milliseconds)
        {
            await Task.Delay(milliseconds, CancellationToken);
        }
    }

    /// <summary>
    /// 行走状态 - 基本移动模式
    /// </summary>
    public class WalkingState : BaseMovementState
    {
        public override string Name => "行走";

        public WalkingState(CancellationToken cancellationToken) : base(cancellationToken) { }

        public override async Task Move(WaypointForTrack waypoint, Point2f currentPosition, int targetOrientation)
        {
            // 基本移动逻辑
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
            await Delay(100);
        }
    }

    /// <summary>
    /// 疾跑状态 - 长距离移动
    /// </summary>
    public class RunningState : BaseMovementState
    {
        public override string Name => "疾跑";
        private bool _isRunning = false;

        public RunningState(CancellationToken cancellationToken) : base(cancellationToken) { }

        public override async Task EnterState()
        {
            await base.EnterState();
            _isRunning = false;
        }

        public override async Task Move(WaypointForTrack waypoint, Point2f currentPosition, int targetOrientation)
        {
            double distance = Navigation.GetDistance(waypoint, currentPosition);
            
            // 距离大于20时使用疾跑
            if (distance > 20 && !_isRunning)
            {
                Simulation.SendInput.SimulateAction(GIActions.SprintMouse, KeyType.KeyDown);
                _isRunning = true;
            }
            else if (distance <= 20 && _isRunning)
            {
                Simulation.SendInput.SimulateAction(GIActions.SprintMouse, KeyType.KeyUp);
                _isRunning = false;
            }
            
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
            await Delay(100);
        }

        public override async Task ExitState()
        {
            if (_isRunning)
            {
                Simulation.SendInput.SimulateAction(GIActions.SprintMouse, KeyType.KeyUp);
            }
            await base.ExitState();
        }
    }

    /// <summary>
    /// 飞行状态 - 滑翔
    /// </summary>
    public class FlyingState : BaseMovementState
    {
        public override string Name => "飞行";
        private bool _isFlying = false;
        private int _flyAttempts = 0;

        public FlyingState(CancellationToken cancellationToken) : base(cancellationToken) { }

        public override async Task EnterState()
        {
            await base.EnterState();
            _isFlying = false;
            _flyAttempts = 0;
        }

        public override async Task Move(WaypointForTrack waypoint, Point2f currentPosition, int targetOrientation)
        {
            using var screen = CaptureToRectArea();
            // 使用图像识别判断是否已经飞行
            _isFlying = Bv.GetMotionStatus(screen) == MotionStatus.Fly;
            
            if (!_isFlying)
            {
                // 尝试进入飞行状态
                Logger.LogInformation($"尝试进入飞行状态，第{_flyAttempts + 1}次");
                Simulation.SendInput.SimulateAction(GIActions.Jump);
                Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
                // 短暂等待以检测状态
                await Delay(500);
                _flyAttempts++;
            }
            
            await Delay(100);
        }
        
        public override async Task ExitState()
        {
            // 确保释放空格键
            Simulation.SendInput.SimulateAction(GIActions.Jump, KeyType.KeyUp);
            await base.ExitState();
        }
    }

    /// <summary>
    /// 攀爬状态 - 爬墙
    /// </summary>
    public class ClimbingState : BaseMovementState
    {
        public override string Name => "攀爬";

        public ClimbingState(CancellationToken cancellationToken) : base(cancellationToken) { }

        public override async Task Move(WaypointForTrack waypoint, Point2f currentPosition, int targetOrientation)
        {
            // 攀爬移动逻辑
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
            await Delay(100);
        }
    }

    /// <summary>
    /// 跳跃状态 - 短跳
    /// </summary>
    public class JumpingState : BaseMovementState
    {
        public override string Name => "跳跃";
        private DateTime _lastJumpTime = DateTime.MinValue;

        public JumpingState(CancellationToken cancellationToken) : base(cancellationToken) { }

        public override async Task Move(WaypointForTrack waypoint, Point2f currentPosition, int targetOrientation)
        {
            // 前进并间隔跳跃
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
            
            if ((DateTime.UtcNow - _lastJumpTime).TotalMilliseconds > 800)
            {
                Simulation.SendInput.SimulateAction(GIActions.Jump);
                _lastJumpTime = DateTime.UtcNow;
            }
            
            await Delay(100);
        }
    }

    /// <summary>
    /// 冲刺状态 - 短距离冲刺
    /// </summary>
    public class DashingState : BaseMovementState
    {
        public override string Name => "冲刺";
        private DateTime _lastDashTime = DateTime.MinValue;

        public DashingState(CancellationToken cancellationToken) : base(cancellationToken) { }

        public override async Task Move(WaypointForTrack waypoint, Point2f currentPosition, int targetOrientation)
        {
            double distance = Navigation.GetDistance(waypoint, currentPosition);
            
            // 基本移动
            Simulation.SendInput.SimulateAction(GIActions.MoveForward, KeyType.KeyDown);
            
            // 距离大于20且冷却完成时冲刺
            if (distance > 20 && (DateTime.UtcNow - _lastDashTime).TotalMilliseconds > 1000)
            {
                _lastDashTime = DateTime.UtcNow;
                Simulation.SendInput.SimulateAction(GIActions.SprintMouse);
            }
            
            await Delay(100);
        }
    }
}
