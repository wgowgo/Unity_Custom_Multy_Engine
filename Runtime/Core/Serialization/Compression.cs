using System;

namespace MyNetEngine.Core.Serialization
{
    /// <summary>
    /// Vector/Quaternion 압축 유틸.
    /// - Quaternion: smallest-3 방식 (최대성분 인덱스 2bit + 3개 성분을 9~12bit로 양자화)
    /// - Vector3: per-축 quantized float
    /// </summary>
    public static class Compression
    {
        // Quaternion smallest-three: 2bit index + 3 * 10bit = 32bit
        private const int CompPrecisionBits = 10;
        private const int CompMax = (1 << CompPrecisionBits) - 1;
        private const float InvSqrt2 = 0.70710678f; // 1/sqrt(2)

        public static void WriteCompressedQuaternion(NetWriter w, float x, float y, float z, float wv)
        {
            // 최대 절댓값 성분 찾기
            float ax = Abs(x), ay = Abs(y), az = Abs(z), aw = Abs(wv);
            int largest = 0;
            float max = ax;
            if (ay > max) { max = ay; largest = 1; }
            if (az > max) { max = az; largest = 2; }
            if (aw > max) { max = aw; largest = 3; }

            // 최대 성분의 부호를 이용해 나머지 성분 부호 통일 (재구성 가능)
            float a, b, c, sign;
            switch (largest)
            {
                case 0: sign = Sign(x); a = y; b = z; c = wv; break;
                case 1: sign = Sign(y); a = x; b = z; c = wv; break;
                case 2: sign = Sign(z); a = x; b = y; c = wv; break;
                default: sign = Sign(wv); a = x; b = y; c = z; break;
            }
            a *= sign; b *= sign; c *= sign;

            uint qa = QuantizeSigned(a, -InvSqrt2, InvSqrt2, CompPrecisionBits);
            uint qb = QuantizeSigned(b, -InvSqrt2, InvSqrt2, CompPrecisionBits);
            uint qc = QuantizeSigned(c, -InvSqrt2, InvSqrt2, CompPrecisionBits);

            // pack: [largest:2][qa:10][qb:10][qc:10] = 32bit
            uint packed = (uint)largest;
            packed |= qa << 2;
            packed |= qb << (2 + CompPrecisionBits);
            packed |= qc << (2 + 2 * CompPrecisionBits);
            w.WriteUInt(packed);
        }

        public static void ReadCompressedQuaternion(NetReader r, out float x, out float y, out float z, out float wv)
        {
            uint packed = r.ReadUInt();
            int largest = (int)(packed & 0x3);
            uint qa = (packed >> 2) & (uint)CompMax;
            uint qb = (packed >> (2 + CompPrecisionBits)) & (uint)CompMax;
            uint qc = (packed >> (2 + 2 * CompPrecisionBits)) & (uint)CompMax;

            float a = DequantizeSigned(qa, -InvSqrt2, InvSqrt2, CompPrecisionBits);
            float b = DequantizeSigned(qb, -InvSqrt2, InvSqrt2, CompPrecisionBits);
            float c = DequantizeSigned(qc, -InvSqrt2, InvSqrt2, CompPrecisionBits);
            float d = (float)Math.Sqrt(Math.Max(0f, 1f - (a * a + b * b + c * c)));

            switch (largest)
            {
                case 0: x = d; y = a; z = b; wv = c; break;
                case 1: x = a; y = d; z = b; wv = c; break;
                case 2: x = a; y = b; z = d; wv = c; break;
                default: x = a; y = b; z = c; wv = d; break;
            }
        }

        public static uint QuantizeSigned(float v, float min, float max, int bits)
        {
            float t = (v - min) / (max - min);
            if (t < 0) t = 0; else if (t > 1) t = 1;
            uint range = (uint)((1 << bits) - 1);
            return (uint)(t * range + 0.5f);
        }

        public static float DequantizeSigned(uint q, float min, float max, int bits)
        {
            uint range = (uint)((1 << bits) - 1);
            float t = (float)q / range;
            return min + t * (max - min);
        }

        private static float Abs(float v) => v < 0 ? -v : v;
        private static float Sign(float v) => v < 0 ? -1f : 1f;
    }
}
