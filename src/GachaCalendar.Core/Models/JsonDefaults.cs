using System.Text.Json;
using System.Text.Json.Serialization;

namespace GachaCalendar.Core.Models;

/// <summary>
/// 全局 JSON 口径：feed / catalog / settings 都是"消费方忽略未知字段、缺失字段取默认值"。
/// 统一放这里，避免每个模型各写一套 options 出现行为差异。
/// </summary>
public static class JsonDefaults
{
	public static readonly JsonSerializerOptions Read = new()
	{
		PropertyNameCaseInsensitive = true,
		AllowTrailingCommas = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		NumberHandling = JsonNumberHandling.AllowReadingFromString,
	};

	public static readonly JsonSerializerOptions Write = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};
}
