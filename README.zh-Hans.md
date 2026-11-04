[English](README.md) | [ไทย](README.th.md) | **简体中文**

# AvatarTuner

AvatarTuner 在 Unity Editor 中分析并优化 VRChat 头像的纹理内存。它按需生成纹理、材质和动画资源的副本，不修改原始资源，也不减少三角形或合并网格、材质。

## 安装与状态

2.0.0 尚在准备中，尚未发布。请在使用 Unity 2022.3.22f1 的 VCC 项目中将此检出目录作为本地包安装以进行验证。[VCC 仓库](https://awakenginexe.github.io/AvatarTuner/index.json)和 [GitHub Releases](https://github.com/awakenginexe/AvatarTuner/releases)提供已发布版本，目前不要假定其中已有 2.0.0。VRChat SDK 不是编译时依赖；NDMF 可选，用于构建时应用。

## 使用

打开 **Tools → AvatarTuner**，选择场景头像并分析，打开优化工作区。使用 Quick 或 Advanced 设置，主动评估候选项，检查所选纹理预览、差异和内存估算后再确认。评估可以取消。生成文件位于 `Assets/AvatarTuner-Generated/`；场景应用支持 Undo 和恢复。可安全重映射的材质切换使用生成的动画片段及控制器副本；不安全的情况保留原资源并报告限制。

设置按头像保存在项目 `Library` 中。创建 Category 或 Custom 覆盖时会复制当时继承的整套设置，之后父级变化不会改变该快照；重置会恢复继承。更改 Quick 质量阈值会保留 Custom 覆盖。

自适应规划选择满足质量限制的最小候选项；内存比例只是目标，可能无法达到。Standard 使用 Unity Mitchell 和导入压缩质量 50；Best Quality 使用质量 100，比较 Unity Mitchell/Bilinear，并为 PNG/JPEG 比较 Area、Lanczos3 和 Mitchell。压缩使用 Unity 原生导入器，不使用外部编码器。

无法安全验证导入候选项时，HDR、法线贴图和形状不明确的纹理保持不变。BC6H 用于内存计算和报告，不提供新的 HDR 转换。ASTC 4x4/6x6/8x8 仅适用于 Android，要求当前构建目标为 Android 且支持解码。

纹理内存采用 VRChat SDK 的格式/mip 算法和 MB 单位约定（字节 / 1,048,576）。当前头像内存优先读取已安装 SDK 的性能计算值，无法读取时会标记为按 SDK 公式估算。计划和导入结果总计仍是估算值；共享纹理只计一次，未知格式或形状会使总计不完整。规划清单可能包含超出 SDK 当前渲染器引用范围的动画纹理。这些值不是构建后头像、下载或磁盘大小的实测值。旧内存总计需要重新分析，已生成资源和恢复信息会保留。

2.0.0 尚未在 Unity 或 VRChat 中验证。请按[手动测试清单](docs/Manual-Testing-2.0.0.md)记录实际结果。另见[更新日志](CHANGELOG.md)、[第三方声明](THIRD_PARTY_NOTICES.md)和 [MIT 许可证](LICENSE)。
