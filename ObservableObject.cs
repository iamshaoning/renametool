using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RenameTool;

/// <summary>可观察对象基类（简化 MVVM，无第三方依赖）。</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
	public event PropertyChangedEventHandler? PropertyChanged;

	protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
	{
		if (EqualityComparer<T>.Default.Equals(field, value)) return false;
		field = value;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		return true;
	}

	protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
		=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
