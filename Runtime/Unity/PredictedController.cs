using MyNetEngine.Core.Serialization;
using MyNetEngine.Prediction;

namespace MyNetEngine.Unity
{
    /// <summary>
    /// character motor용 결정론적 예측 컨트롤러 예시.
    /// Tier1 prediction: kinematic. rigidbody/physics 의존 최소화.
    /// </summary>
    public struct PlayerInputData
    {
        public float MoveX, MoveZ;
        public bool Jump;
    }

    public struct PlayerState
    {
        public float Px, Py, Pz;
        public float Vy;
        public bool Grounded;
    }

    public sealed class PredictedController : PredictedBehaviour<PlayerInputData, PlayerState>
    {
        public float MoveSpeed = 5f;
        public float JumpSpeed = 6f;
        public float Gravity = -18f;
        public float GroundY = 0f;

        protected override void Simulate(ref PlayerInputData input, ref PlayerState state, float dt)
        {
            state.Px += input.MoveX * MoveSpeed * dt;
            state.Pz += input.MoveZ * MoveSpeed * dt;

            if (state.Grounded && input.Jump)
            {
                state.Vy = JumpSpeed;
                state.Grounded = false;
            }

            state.Vy += Gravity * dt;
            state.Py += state.Vy * dt;

            if (state.Py <= GroundY)
            {
                state.Py = GroundY;
                state.Vy = 0;
                state.Grounded = true;
            }
        }

        protected override bool NeedsReconcile(in PlayerState serverState, in PlayerState predictedState)
        {
            return Reconciliation.DistanceExceeds(
                serverState.Px, serverState.Py, serverState.Pz,
                predictedState.Px, predictedState.Py, predictedState.Pz,
                0.15f);
        }

        public override void Serialize(NetWriter w, bool isInitial)
        {
            w.WriteFloat(CurrentState.Px);
            w.WriteFloat(CurrentState.Py);
            w.WriteFloat(CurrentState.Pz);
            w.WriteFloat(CurrentState.Vy);
            w.WriteBool(CurrentState.Grounded);
        }

        public override void Deserialize(NetReader r, bool isInitial)
        {
            var s = new PlayerState
            {
                Px = r.ReadFloat(),
                Py = r.ReadFloat(),
                Pz = r.ReadFloat(),
                Vy = r.ReadFloat(),
                Grounded = r.ReadBool()
            };
            CurrentState = s;
            if (NetworkObject != null)
            {
                NetworkObject.PosX = s.Px; NetworkObject.PosY = s.Py; NetworkObject.PosZ = s.Pz;
            }
        }
    }
}
