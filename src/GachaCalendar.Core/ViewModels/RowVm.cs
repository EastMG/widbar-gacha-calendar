using System.ComponentModel;
using System.Runtime.CompilerServices;
using GachaCalendar.Core.Models;

namespace GachaCalendar.Core.ViewModels;

/// <summary>
/// 面板里的一行（一款游戏）。纯 C# / INotifyPropertyChanged，不含任何图标/画刷等 UI 类型；
/// 图标 URL 通过 <see cref="IconUrl"/> 交给外壳自己决定怎么加载。
/// </summary>
public sealed class RowVm : INotifyPropertyChanged
{
	private readonly CatalogEntry _game;

	private string _badgeText = "";
	private string _badgeKind = "";
	private bool _hasBadge;
	private string _gachaText = "—";
	private string _gachaTip = "";
	private string _gachaDateText = "";
	private string _gachaDateTip = "";
	private string _eventText = "—";
	private string _eventTip = "";
	private string _eventDateText = "";
	private string _eventDateTip = "";
	private bool _showGacha = true;
	private bool _showEvent = true;

	public RowVm(CatalogEntry game, GameRecord? rec)
	{
		_game = game;
		Record = rec;
		Name = game.Name;
		// 与 WPF 版一致：构造时先用当前时间填一遍，随后由 Tick(now) 覆盖成注入时间。
		Refresh(DateTime.Now);
	}

	public string Name { get; }

	public string GameId => _game.Id;

	public string IconUrl => _game.Icon;

	public CatalogEntry Game => _game;

	public GameRecord? Record { get; }

	public string BadgeText { get => _badgeText; private set => Set(ref _badgeText, value); }
	public string BadgeKind { get => _badgeKind; private set => Set(ref _badgeKind, value); }
	public bool HasBadge { get => _hasBadge; private set => Set(ref _hasBadge, value); }
	public string GachaText { get => _gachaText; private set => Set(ref _gachaText, value); }
	public string GachaTip { get => _gachaTip; private set => Set(ref _gachaTip, value); }
	public string GachaDateText { get => _gachaDateText; private set => Set(ref _gachaDateText, value); }
	public string GachaDateTip { get => _gachaDateTip; private set => Set(ref _gachaDateTip, value); }
	public string EventText { get => _eventText; private set => Set(ref _eventText, value); }
	public string EventTip { get => _eventTip; private set => Set(ref _eventTip, value); }
	public string EventDateText { get => _eventDateText; private set => Set(ref _eventDateText, value); }
	public string EventDateTip { get => _eventDateTip; private set => Set(ref _eventDateTip, value); }

	/// <summary>外壳的两列开关（settings.showGacha / showEvent），由 WidgetViewModel 同步。</summary>
	public bool ShowGacha { get => _showGacha; internal set => Set(ref _showGacha, value); }

	public bool ShowEvent { get => _showEvent; internal set => Set(ref _showEvent, value); }

	public void Refresh(DateTime now)
	{
		var d = RowBuilder.Build(_game, Record, now);
		BadgeText = d.BadgeText;
		BadgeKind = d.BadgeKind;
		HasBadge = d.HasBadge;
		GachaText = d.GachaText;
		GachaTip = d.GachaTip;
		GachaDateText = d.GachaDateText;
		GachaDateTip = d.GachaDateTip;
		EventText = d.EventText;
		EventTip = d.EventTip;
		EventDateText = d.EventDateText;
		EventDateTip = d.EventDateTip;
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, value)) return;
		field = value;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}
}
