using System.Collections.Generic;
using MyNetEngine.Core.Serialization;
using MyNetEngine.Objects;

namespace MyNetEngine.Prediction
{
    /// <summary>
    /// 고급 API: owner prediction + server reconciliation.
    /// 사용자 정의 Simulate(input, state, dt)로 결정론적 시뮬레이션 구현.
    ///
    /// 흐름(클라, owner):
    ///   1) tick에 입력 수집 -> InputBuffer
    ///   2) Simulate() 로컬 적용 + state history에 저장
    ///   3) 서버로 input 송신
    ///
    /// 흐름(서버):
    ///   1) 입력 수신 -> tick에 맞춰 Simulate
    ///   2) 결과 state를 authoritative 상태로 덮어씀
    ///   3) client에 snapshot 전송
    ///
    /// 흐름(클라, reconcile):
    ///   1) 서버 state 도착 (serverTick 기준)
    ///   2) 해당 tick 내 예측 state와 비교
    ///   3) 오차 초과 시 state를 서버값으로 되돌리고
    ///      serverTick+1..currentTick까지 InputBuffer 재적용하여 재시뮬레이션
    /// </summary>
    public abstract class PredictedBehaviour<TInput, TState> : NetBehaviour
        where TInput : struct
        where TState : struct
    {
        protected readonly InputBuffer<TInput> Inputs = new InputBuffer<TInput>(128);

        private readonly Dictionary<uint, TState> _history = new Dictionary<uint, TState>();
        protected TState CurrentState;
        protected uint LastAuthoritativeTick;

        /// <summary>
        /// 결정론적 시뮬레이션 스텝. 이 함수는 서버/클라 동일해야 한다.
        /// </summary>
        protected abstract void Simulate(ref TInput input, ref TState state, float dt);

        /// <summary>
        /// 오차 판정. 기본은 구조체 비교. 대개 override.
        /// </summary>
        protected virtual bool NeedsReconcile(in TState serverState, in TState predictedState) => false;

        /// <summary>
        /// 서버 상태 수신 및 reconciliation 수행.
        /// </summary>
        public void OnServerState(uint serverTick, in TState serverState, uint currentClientTick, float dt)
        {
            LastAuthoritativeTick = serverTick;
            if (_history.TryGetValue(serverTick, out var predicted)
                && !NeedsReconcile(serverState, predicted))
            {
                // 일치. aged state 정리.
                PruneBefore(serverTick);
                return;
            }

            // reconcile: state 되돌리고 재시뮬레이션
            CurrentState = serverState;
            _history[serverTick] = serverState;
            for (uint t = serverTick + 1; t <= currentClientTick; t++)
            {
                if (Inputs.TryGet(t, out var inp))
                {
                    Simulate(ref inp, ref CurrentState, dt);
                }
                _history[t] = CurrentState;
            }
            PruneBefore(serverTick);
        }

        /// <summary>
        /// 클라 owner: 이번 tick에 입력 수집/적용.
        /// </summary>
        public void ClientTick(uint tick, in TInput input, float dt)
        {
            Inputs.Set(tick, input);
            var inpLocal = input;
            Simulate(ref inpLocal, ref CurrentState, dt);
            _history[tick] = CurrentState;
            PruneBefore(tick > 120 ? tick - 120 : 0);
        }

        /// <summary>
        /// 서버: 수신된 입력으로 tick 진행.
        /// </summary>
        public void ServerTick(uint tick, in TInput input, float dt)
        {
            var inpLocal = input;
            Simulate(ref inpLocal, ref CurrentState, dt);
        }

        private void PruneBefore(uint tick)
        {
            if (_history.Count < 32) return;
            var old = new List<uint>();
            foreach (var k in _history.Keys) if (k < tick) old.Add(k);
            foreach (var k in old) _history.Remove(k);
        }

        public override void Serialize(NetWriter w, bool isInitial)
        {
            // 기본은 없음. 사용자 구현에서 CurrentState 필드를 직렬화.
        }
    }
}
