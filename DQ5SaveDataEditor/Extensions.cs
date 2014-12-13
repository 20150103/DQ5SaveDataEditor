using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DQ5SaveDataEditor
{
	public static class Extensions
	{
		public static string FormatEx(this string str, params object[] args)
		{
			return string.Format(str, args);
		}
	}
}
