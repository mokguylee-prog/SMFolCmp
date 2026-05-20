using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace SMFolCmp.Models
{
    public enum CompareStatus { Identical, DateOnly, Modified, LeftOnly, RightOnly }

    public class FileItem : INotifyPropertyChanged
    {
        private CompareStatus _status;
        public string Name { get; set; }
        public string LeftPath { get; set; }
        public string RightPath { get; set; }
        public bool IsDirectory { get; set; }
        public long LeftSize { get; set; }
        public long RightSize { get; set; }
        public DateTime LeftModified { get; set; }
        public DateTime RightModified { get; set; }

        public CompareStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(StatusColor)); OnPropertyChanged(nameof(RowBackground)); OnPropertyChanged(nameof(LeftRowBackground)); OnPropertyChanged(nameof(RightRowBackground)); }
        }

        public string StatusText => Status switch
        {
            CompareStatus.Identical => "Identical", CompareStatus.DateOnly => "Date Only", CompareStatus.Modified => "Modified",
            CompareStatus.LeftOnly  => "Left Only", CompareStatus.RightOnly => "Right Only", _ => ""
        };

        public Brush StatusColor => Status switch
        {
            CompareStatus.Identical => new SolidColorBrush(Color.FromRgb(160, 160, 160)),
            CompareStatus.DateOnly  => new SolidColorBrush(Color.FromRgb(255, 170, 64)),
            CompareStatus.Modified  => new SolidColorBrush(Color.FromRgb(255, 160, 80)),
            CompareStatus.LeftOnly  => new SolidColorBrush(Color.FromRgb(100, 180, 220)),
            CompareStatus.RightOnly => new SolidColorBrush(Color.FromRgb(100, 220, 100)),
            _ => new SolidColorBrush(Color.FromRgb(160, 160, 160))
        };

        public Brush RowBackground => IsCompareToSource
            ? new SolidColorBrush(Color.FromRgb(0, 100, 200))
            : (Status switch
        {
            CompareStatus.Identical => new SolidColorBrush(Color.FromRgb(45, 45, 48)),
            CompareStatus.DateOnly  => new SolidColorBrush(Color.FromRgb(112, 72, 24)),
            CompareStatus.Modified  => new SolidColorBrush(Color.FromRgb(120, 80, 40)),
            CompareStatus.LeftOnly  => new SolidColorBrush(Color.FromRgb(60, 100, 140)),
            CompareStatus.RightOnly => new SolidColorBrush(Color.FromRgb(60, 120, 60)),
            _ => new SolidColorBrush(Color.FromRgb(45, 45, 48))
        });

        public Brush LeftRowBackground => IsCompareToSource
            ? new SolidColorBrush(Color.FromRgb(0, 100, 200))
            : (Status switch
        {
            CompareStatus.Modified  => new SolidColorBrush(Color.FromRgb(120, 80, 40)),
            CompareStatus.DateOnly  => new SolidColorBrush(Color.FromRgb(112, 72, 24)),
            CompareStatus.LeftOnly  => new SolidColorBrush(Color.FromRgb(60, 100, 140)),
            CompareStatus.RightOnly => new SolidColorBrush(Color.FromRgb(70, 70, 70)),
            _ => new SolidColorBrush(Color.FromRgb(45, 45, 48))
        });

        public Brush RightRowBackground => IsCompareToSource
            ? new SolidColorBrush(Color.FromRgb(0, 100, 200))
            : (Status switch
        {
            CompareStatus.Modified  => new SolidColorBrush(Color.FromRgb(120, 80, 40)),
            CompareStatus.DateOnly  => new SolidColorBrush(Color.FromRgb(112, 72, 24)),
            CompareStatus.RightOnly => new SolidColorBrush(Color.FromRgb(60, 120, 60)),
            CompareStatus.LeftOnly  => new SolidColorBrush(Color.FromRgb(70, 70, 70)),
            _ => new SolidColorBrush(Color.FromRgb(45, 45, 48))
        });

        public Brush TextColor => Status switch
        {
            CompareStatus.Modified  => new SolidColorBrush(Color.FromRgb(255, 100, 100)),
            CompareStatus.DateOnly  => new SolidColorBrush(Color.FromRgb(255, 190, 96)),
            CompareStatus.LeftOnly  => new SolidColorBrush(Color.FromRgb(120, 180, 255)),
            CompareStatus.RightOnly => new SolidColorBrush(Color.FromRgb(120, 180, 255)),
            _ => new SolidColorBrush(Color.FromRgb(224, 224, 224))
        };

        public string LeftDisplayName => LeftPath != null ? Name : "";
        public string RightDisplayName => RightPath != null ? Name : "";
        public string LeftSizeText  => IsDirectory ? "<Folder>" : (LeftPath  != null ? FormatSize(LeftSize)  : "");
        public string RightSizeText => IsDirectory ? "<Folder>" : (RightPath != null ? FormatSize(RightSize) : "");
        public string LeftModifiedText  => LeftPath  != null ? LeftModified.ToString("yyyy-MM-dd HH:mm:ss")  : "";
        public string RightModifiedText => RightPath != null ? RightModified.ToString("yyyy-MM-dd HH:mm:ss") : "";
        public string Icon => IsDirectory ? (IsExpanded ? "\uE838" : "\uE8B7") : "";
        public Brush IconBackground => Brushes.Transparent;
        public Brush IconForeground => IsDirectory ? new SolidColorBrush(Color.FromRgb(244, 184, 72)) : Brushes.Transparent;
        public bool IsFile => !IsDirectory;
        public Brush FileMarkerBrush => TextColor;

        public List<FileItem> Children { get; set; } = new();
        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; OnPropertyChanged(nameof(IsExpanded)); OnPropertyChanged(nameof(Icon)); }
        }
        private bool _isCompareToSource;
        public bool IsCompareToSource
        {
            get => _isCompareToSource;
            set { _isCompareToSource = value; OnPropertyChanged(nameof(IsCompareToSource)); OnPropertyChanged(nameof(RowBackground)); OnPropertyChanged(nameof(LeftRowBackground)); OnPropertyChanged(nameof(RightRowBackground)); }
        }
        public int Depth { get; set; } = 0;
        public bool IsLastVisibleSibling { get; set; }
        public List<bool> AncestorHasNextVisibleSibling { get; set; } = new();
        public string ExpandIcon => "";
        public bool HasChildren => IsDirectory && Children.Count > 0;
        public string TreeLineString
        {
            get
            {
                if (Depth == 0) return "";
                var lines = "";
                for (int i = 0; i < Depth - 1 && i < AncestorHasNextVisibleSibling.Count; i++)
                {
                    var hasNextSibling = AncestorHasNextVisibleSibling[i];
                    lines += hasNextSibling ? "│  " : "   ";
                }
                return lines + (IsLastVisibleSibling ? "└─ " : "├─ ");
            }
        }

        public string LeftTreeLineString => LeftPath != null ? TreeLineString : "";
        public string RightTreeLineString => RightPath != null ? TreeLineString : "";

        private static string FormatSize(long b)
        {
            if (b < 1024) return $"{b} B";
            if (b < 1048576) return $"{b/1024.0:F1} KB";
            if (b < 1073741824) return $"{b/1048576.0:F1} MB";
            return $"{b/1073741824.0:F1} GB";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
