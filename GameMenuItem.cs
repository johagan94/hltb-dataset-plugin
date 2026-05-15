using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Unbroken.LaunchBox.Plugins.Data;

namespace HltbDatasetPlugin;

public class GameMenuItem : IGameMenuItem
{
    public string Caption { get; }
    public IEnumerable<IGameMenuItem> Children { get; }
    public bool Enabled { get; }
    public Image Icon { get; set; }
    public Action<IGame[]>? OnSelectAction { get; }

    public GameMenuItem(string caption, Action<IGame[]>? onSelect = null, bool enabled = true, Image? icon = null,
        IEnumerable<IGameMenuItem>? children = null)
    {
        Caption = caption;
        OnSelectAction = onSelect;
        Enabled = enabled;
        Icon = icon ?? CreateDefaultIcon();
        Children = children ?? Enumerable.Empty<IGameMenuItem>();
    }

    public void OnSelect(params IGame[] selectedGames)
    {
        OnSelectAction?.Invoke(selectedGames);
    }

    private static Image CreateDefaultIcon()
    {
        var bmp = new Bitmap(1, 1);
        return bmp;
    }
}
