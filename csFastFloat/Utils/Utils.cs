﻿using csFastFloat.Constants;
using csFastFloat.Structures;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;


#if HAS_INTRINSICS
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
#endif

[assembly: InternalsVisibleTo("TestcsFastFloat")]

namespace csFastFloat
{

  internal static unsafe class Utils
  {
#if !HAS_BITOPERATIONS
    private static ReadOnlySpan<byte> Log2DeBruijn => new byte[]
    {
      00, 09, 01, 10, 13, 21, 02, 29,
      11, 14, 16, 18, 22, 25, 03, 30,
      08, 12, 20, 28, 15, 17, 24, 07,
      19, 27, 23, 06, 26, 05, 04, 31
    };
#endif
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static uint parse_eight_digits_unrolled(ulong val)
    {
      const ulong mask = 0x000000FF000000FF;
      const ulong mul1 = 0x000F424000000064; // 100 + (1000000ULL << 32)
      const ulong mul2 = 0x0000271000000001; // 1 + (10000ULL << 32)
      val -= 0x3030303030303030;
      val = (val * 10) + (val >> 8); // val = (val * 2561) >> 8;
      val = (((val & mask) * mul1) + (((val >> 16) & mask) * mul2)) >> 32;
      return (uint)val;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static  uint parse_eight_digits_unrolled(byte* chars)
    {
      ulong val = Unsafe.ReadUnaligned<ulong>(chars);
      return parse_eight_digits_unrolled(val);
    }

    

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static  bool is_made_of_eight_digits_fast(byte* chars)
    {
      ulong val = Unsafe.ReadUnaligned<ulong>(chars);
      // We only enable paths depending on this function on little endian
      // platforms (it happens to be effectively nearly everywhere).
      // ref : https://lemire.me/blog/tag/swar/
      return BitConverter.IsLittleEndian && ((val & (val + 0x0606060606060606) & 0xF0F0F0F0F0F0F0F0) == 0x3030303030303030);

    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool is_integer(char c, out uint cMinus0)
    {
      uint cc = (uint)(c - '0');
      bool res = cc <= '9' - '0';
      cMinus0 = cc;
      return res;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool is_integer(byte c, out uint cMinus0)
    {
      uint cc = (uint)(c - (byte)'0');
      bool res = cc <= '9' - '0';
      cMinus0 = cc;
      return res;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static value128 compute_product_approximation(int bitPrecision, long q, ulong w)
    {
      int index = 2 * (int)(q - CalculationConstants.smallest_power_of_five);
      // For small values of q, e.g., q in [0,27], the answer is always exact because
      // The line value128 firstproduct = full_multiplication(w, power_of_five_128[index]);
      // gives the exact answer.
      value128 firstproduct = FullMultiplication(w, CalculationConstants.get_power_of_five_128(index));
      //static_assert((bit_precision >= 0) && (bit_precision <= 64), " precision should  be in (0,64]");
      ulong precision_mask = (bitPrecision < 64) ? ((ulong)(0xFFFFFFFFFFFFFFFF) >> bitPrecision) : (ulong)(0xFFFFFFFFFFFFFFFF);
      if ((firstproduct.high & precision_mask) == precision_mask)
      { // could further guard with  (lower + w < lower)
        // regarding the second product, we only need secondproduct.high, but our expectation is that the compiler will optimize this extra work away if needed.
        value128 secondproduct = FullMultiplication(w, CalculationConstants.get_power_of_five_128(index + 1));
        firstproduct.low += secondproduct.high;
        if (secondproduct.high > firstproduct.low)
        {
          firstproduct.high++;
        }
      }
      return firstproduct;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int power(int q)
        => (((152170 + 65536) * q) >> 16) + 63;

#if NET5_0_OR_GREATER

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static value128 FullMultiplication(ulong value1, ulong value2)
    {
      ulong hi = Math.BigMul(value1, value2, out ulong lo);
      return new value128(hi, lo);
    }

#else
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static  value128 FullMultiplication(ulong value1, ulong value2)
    {
#if HAS_INTRINSICS
      if(System.Runtime.Intrinsics.X86.Bmi2.X64.IsSupported)
      {
            ulong lo;
            ulong hi = System.Runtime.Intrinsics.X86.Bmi2.X64.MultiplyNoFlags(value1, value2, &lo);
            return new value128(hi, lo);
      }
#endif
      return Emulate64x64to128( value1, value2);
    }


    internal static value128 Emulate64x64to128(ulong x, ulong y)
    {
      ulong x0 = (uint)x, x1 = x >> 32;
      ulong y0 = (uint)y, y1 = y >> 32;
      ulong p11 = x1 * y1, p01 = x0 * y1;
      ulong p10 = x1 * y0, p00 = x0 * y0;

      ulong middle = p10 + (p00 >> 32) + (uint)p01;

      return new value128(h: p11 + (middle >> 32) + (p01 >> 32), l: (middle << 32) | (uint)p00);
    }

#endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool is_ascii_space(char c)
    {
        // ROS for one byte types can be read directly from metadata avoiding the array allocation.
        ReadOnlySpan<bool> table = new bool[] {
            false, false, false, false, false, false, false, false, false, true, true, true, true, true, false, false, false, false, false, false, false, false, false, false,
            false, false, false, false, false, false, false, false, true};
        // Avoid bound checking.
        return (c >32) ? false : Unsafe.AddByteOffset(ref MemoryMarshal.GetReference(table), (nint)c);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool is_space(byte c)
    {
        // ROS for one byte types can be read directly from metadata avoiding the array allocation.
        ReadOnlySpan<bool> table = new bool[] {
            false, false, false, false, false, false, false, false, false, true, true, true, true, true, false, false, false, false, false, false, false, false, false, false,
            false, false, false, false, false, false, false, false, true, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false,
            false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false,
            false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false,
            false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false,
            false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false,
            false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false,
            false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false,
            false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false,
            false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false,
            false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false};
        
        // Avoid bound checking.
        return Unsafe.AddByteOffset(ref MemoryMarshal.GetReference(table), (nint)c);
    }

    [ExcludeFromCodeCoverage]
    internal static  bool strncasecmp(char* input1, string input2, int length)
    {
      fixed (char* p2 = input2)
      {
        return strncasecmp(input1, p2, length);
      }
    }

    internal static  bool strncasecmp(char* input1, char* input2, int length)
    {
      int running_diff = 0;

      for (int i = 0; i < length; i++)
      {
        running_diff |= (input1[i] ^ input2[i]);
      }
      return (running_diff == 0) || (running_diff == 32);
    }
    internal static  bool strncasecmp(byte* input1, ReadOnlySpan<byte> input2, int length)
    {
      int running_diff = 0;

      for (int i = 0; i < length; i++)
      {
        running_diff |= (input1[i] ^ input2[i]);
      }
      return (running_diff == 0) || (running_diff == 32);
    }

    

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int LeadingZeroCount(ulong value)
    {
#if HAS_BITOPERATIONS
      return System.Numerics.BitOperations.LeadingZeroCount(value);
#else
      uint hi = (uint)(value >> 32);
 
      if (hi == 0)
      {
        return 32 + Log2SoftwareFallback((uint)value);
      }
 
      return Log2SoftwareFallback(hi);
      
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      static int Log2SoftwareFallback(uint value)
      {
        if (value == 0)
        {
          return 32;
        }
        
        int n = 1;
        if (value >> 16 == 0) { n += 16; value <<= 16; }
        if (value >> 24 == 0) { n +=  8; value <<=  8; }
        if (value >> 28 == 0) { n +=  4; value <<=  4; }
        if (value >> 30 == 0) { n +=  2; value <<=  2; }
        n -= (int) (value >> 31);
        return n;
    
        }
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static  float Int32BitsToSingle(int value)
#if HAS_BITOPERATIONS
      => BitConverter.Int32BitsToSingle(value);
#else
      => *((float*)&value);
#endif





#if HAS_INTRINSICS

    /// <summary>
    /// Detect eight consecutive digits and parse them a an unsigned int using SIMD instructions
    /// </summary>
    /// <param name="start">pointer to the sequence of char to evaluate</param>
    /// <param name="value">out : parsed value</param>
    /// <returns>bool : succes of operation : true meaning the sequence contains at least 8 consecutive digits</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static  bool TryParseEightConsecutiveDigits_SIMD(char* start, out uint value)
    {

      // escape if SIMD functions aren't available.
      if (!Sse41.IsSupported)
      {
        value = 0;
        return false;
      }


      value = 0;
      Vector128<short> raw = Sse41.LoadDquVector128((short*)start);
      Vector128<short> ascii0 = Vector128.Create((short)(48 + short.MinValue));
      Vector128<short> after_ascii9 = Vector128.Create((short)(short.MinValue + 9));
      Vector128<short> a = Sse41.Subtract(raw, ascii0);
      Vector128<short> b = Sse41.CompareLessThan( after_ascii9, a);

      if (!Sse41.TestZ(b, b))
      {
        return false;
      }

      // @Credit  AQRIT
      // https://stackoverflow.com/questions/66371621/hardware-simd-parsing-in-c-sharp-performance-improvement/66430672
      Vector128<byte> mul1 = Vector128.Create(0x14C814C8, 0x010A0A64, 0, 0).AsByte();
      Vector128<short> mul2 = Vector128.Create(0x00FA61A8, 0x0001000A, 0, 0).AsInt16();

      //  extract the low bytes of each 16-bit word
      var vb = Sse41.Shuffle( a.AsByte(), Vector128.Create(0, 2, 4, 6, 8, 10, 12, 14, 0, 2, 4, 6, 8, 10, 12, 14).AsByte());
      Vector128<int> v = Sse2.MultiplyAddAdjacent(Ssse3.MultiplyAddAdjacent(mul1, vb.AsSByte()), mul2);
      v = Sse2.Add(Sse2.Add(v, v), Sse2.Shuffle(v, 1));
      value = (uint)v.GetElement(0);

      return true;

    }

  
#endif

  }
}
