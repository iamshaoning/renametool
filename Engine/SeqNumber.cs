using System.Text;

namespace RenameTool.Engine;

/// <summary>序号数字到文本的格式化（数字补零 / 字母 / 罗马数字）。</summary>
public static class SeqNumber
{
	public static string Format(long value, Models.SeqType type, int padding)
	{
		switch (type)
		{
			case Models.SeqType.Alpha:
				return ToAlpha(value);
			case Models.SeqType.Roman:
				return value > 0 && value < 4000 ? ToRoman((int)value) : value.ToString();
			default:
				// 位宽限幅：避免用户填入极端值导致 PadLeft 申请超大字符串
				int width = Math.Clamp(padding, 1, 32);
				// 不用 Math.Abs：long.MinValue 取绝对值会溢出抛异常
				bool negative = value < 0;
				ulong magnitude = negative ? unchecked((ulong)(-(value + 1))) + 1 : (ulong)value;
				string digits = magnitude.ToString().PadLeft(width, '0');
				return negative ? "-" + digits : digits;
		}
	}

	/// <summary>1→A … 26→Z, 27→AA（类 Excel 列号）。</summary>
	public static string ToAlpha(long value)
	{
		if (value <= 0) return value.ToString();
		var sb = new StringBuilder();
		long v = value;
		while (v > 0)
		{
			v--;
			sb.Insert(0, (char)('A' + (int)(v % 26)));
			v /= 26;
		}
		return sb.ToString();
	}

	private static readonly (int, string)[] RomanTable =
	[
		(1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
		(100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
		(10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I"),
	];

	public static string ToRoman(int value)
	{
		var sb = new StringBuilder();
		int v = value;
		foreach ((int n, string s) in RomanTable)
		{
			while (v >= n)
			{
				sb.Append(s);
				v -= n;
			}
		}
		return sb.ToString();
	}
}
