using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace GachaWidget.Diagnostics;

/// <summary>
/// 启动诊断：把当前进程的令牌关键属性写进 startup.log。
/// 用途：判断 WidBar 拉起的 widget 进程是否有"包身份 + 完整用户令牌"。
/// - 若 <see cref="IsAppContainer"/> 为 true，用户 SID 在令牌里是 deny-only，
///   连不上 WidBar 用 PipeOptions.CurrentUserOnly 创建的宿主管道（实测症状：Access denied）。
/// </summary>
internal static class TokenInfo
{
	private const uint TokenQuery = 0x0008;
	private const int TokenIsAppContainer = 29;
	private const int TokenIntegrityLevel = 25;

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern IntPtr GetCurrentProcess();

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool GetTokenInformation(IntPtr token, int infoClass, out int buffer, int bufferLength, out int returnLength);

	[DllImport("advapi32.dll", SetLastError = true)]
	private static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr buffer, int bufferLength, out int returnLength);

	[DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool ConvertSidToStringSidW(IntPtr sid, out IntPtr stringSid);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool CloseHandle(IntPtr handle);

	[DllImport("kernel32.dll")]
	private static extern IntPtr LocalFree(IntPtr h);

	public static string Describe()
	{
		var sb = new StringBuilder();
		try
		{
			using var identity = WindowsIdentity.GetCurrent();
			sb.Append("user=").Append(identity.User?.Value ?? "?");
			sb.Append("; authType=").Append(identity.AuthenticationType ?? "?");
			sb.Append("; isAppContainer=").Append(identity.IsSystem ? "system" : DescribeAppContainer());
			sb.Append("; integrity=").Append(DescribeIntegrity());
			sb.Append(';').Append("groups=[");
			var first = true;
			var groups = identity.Groups;
			if (groups is not null)
			{
				foreach (var group in groups)
				{
					if (!first) sb.Append(',');
					first = false;
					sb.Append(group.Value);
				}
			}
			sb.Append(']');
		}
		catch (Exception ex)
		{
			sb.Append("token info error: ").Append(ex.Message);
		}

		try
		{
			sb.Append("; isAdmin=").Append(new WindowsPrincipal(WindowsIdentity.GetCurrent())
				.IsInRole(WindowsBuiltInRole.Administrator));
		}
		catch
		{
			// 忽略
		}

		return sb.ToString();
	}

	private static string DescribeAppContainer()
	{
		if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out var token)) return "unknown";
		try
		{
			if (!GetTokenInformation(token, TokenIsAppContainer, out var value, sizeof(int), out _)) return "unknown";
			return value != 0 ? "true" : "false";
		}
		finally
		{
			CloseHandle(token);
		}
	}

	/// <summary>TokenIntegrityLevel：RID 0x1000=Low / 0x2000=Medium / 0x3000=High / 0x4000=System。</summary>
	private static string DescribeIntegrity()
	{
		if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out var token)) return "unknown";
		try
		{
			if (!GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out var needed) || needed <= 0)
			{
				// 第一次调用只为拿所需缓冲区大小，返回 false 是正常的（ERROR_INSUFFICIENT_BUFFER）。
				needed = 64;
			}

			var buffer = Marshal.AllocHGlobal(needed);
			try
			{
				if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, needed, out _)) return "unknown";
				// TOKEN_MANDATORY_LABEL { SID_AND_ATTRIBUTES { PSID Sid; DWORD Attributes } }
				var sid = Marshal.ReadIntPtr(buffer);
				if (!ConvertSidToStringSidW(sid, out var stringSid)) return "unknown";
				try
				{
					var value = Marshal.PtrToStringUni(stringSid) ?? "unknown";
					return value;
				}
				finally
				{
					LocalFree(stringSid);
				}
			}
			finally
			{
				Marshal.FreeHGlobal(buffer);
			}
		}
		finally
		{
			CloseHandle(token);
		}
	}
}
