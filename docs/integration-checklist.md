# 现有 Streamlit 项目接入清单

这个清单对应当前 `D:\androgen_rag` 项目，目标是把它接到这个安装/启动/更新壳层里。

## 必做改造

1. 根目录不能再写死为 `D:\androgen_rag`
2. 所有写入路径必须改到 `APP_DATA_DIR`
3. 所有外部模块路径必须改为优先读取环境变量
4. `launcher.exe` 启动时必须只调用 bundle 内的 Python

## 现阶段已定位到的硬编码点

- `scripts/streamlit_app.py`
  - `DEFAULT_ROOT = Path(r"D:\androgen_rag")`
- `scripts/rescore_enumerated_candidates.py`
  - `DEFAULT_SMARTCYP_JAR = Path(r"D:\external_tools\smartcyp\target\smartcyp.jar")`
  - `DEFAULT_SYGMA_PYTHON = Path(r"D:\miniconda\envs\sygma_env\python.exe")`
  - `DEFAULT_CONDA_EXE = Path(r"D:\miniconda\Scripts\conda.exe")`
- `scripts/predict_cyp1a2_second_opinion.py`
  - `FPGNN_PYTHON = Path(r"D:\miniconda\envs\fpgnn_cyp2\python.exe")`
  - `FPGNN_REPO = Path(r"D:\external_models\FP-GNN_CYP")`
- `pk_models/scripts/predict_pk_panel.py`
  - `PK_ENV_CHEMPROP = r"D:\miniconda\envs\pk_env\Scripts\chemprop.exe"`

## 推荐的环境变量契约

- `APP_INSTALL_DIR`
- `APP_STATE_DIR`
- `APP_DATA_DIR`
- `APP_BUNDLE_DIR`
- `APP_BUNDLE_APP_DIR`
- `APP_VERSION`
- `APP_PORT`
- `SMARTCYP_JAVA`
- `SMARTCYP_JAR`
- `SYGMA_PYTHON`
- `FPGNN_PYTHON`
- `FPGNN_REPO`
- `CHEMPROP_EXE`

## 关于全部模块都保留

v1 推荐 bundle 结构：

```text
bundle/
  runtime/
  app/
    scripts/
    kb/
    pk_models/
  vendor/
    smartcyp/
    fpgnn/
    jre/
```

- `SMARTCyp`：jar 放进 `vendor/smartcyp/`
- `SyGMa`：直接安装进 bundle 自带 Python runtime
- `FPGNN`：代码和模型一起进 `vendor/fpgnn/`
- `chemprop`：装进 bundle 自带 runtime 的 `Scripts/chemprop.exe`

## 风险提示

- 如果 `SyGMa` 或 `RDKit` 依赖非常挑版本，v1 先统一到一个私有 runtime。
- 如果确实无法共存，再退回双 runtime bundle，但这是 v2 备选，不是 v1 默认方案。
