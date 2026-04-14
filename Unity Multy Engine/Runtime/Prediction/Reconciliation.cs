namespace MyNetEngine.Prediction
{
    /// <summary>
    /// 수치 threshold 비교 헬퍼.
    /// </summary>
    public static class Reconciliation
    {
        public static bool DistanceExceeds(float ax, float ay, float az, float bx, float by, float bz, float threshold)
        {
            float dx = ax - bx, dy = ay - by, dz = az - bz;
            return dx * dx + dy * dy + dz * dz > threshold * threshold;
        }

        public static bool AngleExceeds(float a, float b, float thresholdDeg)
        {
            float d = a - b;
            while (d > 180f) d -= 360f;
            while (d < -180f) d += 360f;
            if (d < 0) d = -d;
            return d > thresholdDeg;
        }
    }
}
