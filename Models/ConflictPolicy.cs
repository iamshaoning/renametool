namespace RenameTool.Models;

/// <summary>执行前体检发现命名冲突时的处理策略。</summary>
public enum ConflictPolicy
{
	Skip,        // 跳过冲突项，执行其余
	AutoNumber,  // 为冲突项自动追加序号 (1)(2)…
	Abort,       // 存在冲突则整体中止
}
