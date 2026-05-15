use pyo3::prelude::*;

#[cfg(all(feature = "js-v8", feature = "js-quickjs"))]
compile_error!("features `js-v8` and `js-quickjs` are mutually exclusive");

#[cfg(not(any(feature = "js-v8", feature = "js-quickjs")))]
compile_error!("one of `js-v8` or `js-quickjs` must be enabled");

/// Evaluate `script` inside an isolated JS context and return the captured
/// console.log output as a string.
///
/// The EJS solver emits its JSON result via a single `console.log(JSON.stringify(…))`
/// call. We prepend a shim that redirects console to an array so the output can
/// be returned without a subprocess.
pub fn run_js(script: &str) -> Result<String, String> {
    let src = wrap_script(script);
    run_js_inner(&src)
}

fn wrap_script(script: &str) -> String {
    let mut src = String::with_capacity(script.len() + 300);
    src.push_str(
        "(function(){\
            var __out=[];\
            globalThis.console={\
                log:function(){__out.push([].slice.call(arguments).join(' '));},\
                warn:function(){__out.push([].slice.call(arguments).join(' '));},\
                error:function(){__out.push([].slice.call(arguments).join(' '));}\
            };",
    );
    src.push_str(script);
    src.push_str(";return __out.join('\\n');})()");
    src
}

// ── V8 backend (Windows, macOS) ───────────────────────────────────────────────

#[cfg(feature = "js-v8")]
fn run_js_inner(src: &str) -> Result<String, String> {
    use rustyscript::{Error as JsError, Runtime, RuntimeOptions};

    fn inner(src: &str) -> Result<String, JsError> {
        let mut rt = Runtime::new(RuntimeOptions::default())?;
        rt.eval::<String>(src)
    }

    inner(src).map_err(|e| format!("rustyscript: {e}"))
}

// ── QuickJS backend (Linux, Android, iOS) ────────────────────────────────────

#[cfg(feature = "js-quickjs")]
fn run_js_inner(src: &str) -> Result<String, String> {
    use rquickjs::{Context, Runtime};

    let rt = Runtime::new().map_err(|e| format!("rquickjs init: {e}"))?;
    let ctx = Context::full(&rt).map_err(|e| format!("rquickjs context: {e}"))?;
    ctx.with(|ctx| {
        ctx.eval::<String, _>(src.as_bytes())
            .map_err(|e| format!("rquickjs: {e}"))
    })
}

// ── PyO3 surface ──────────────────────────────────────────────────────────────

#[pyfunction]
#[pyo3(name = "run_js")]
fn py_run_js(_py: Python<'_>, script: String) -> PyResult<String> {
    run_js(&script).map_err(|e| pyo3::exceptions::PyRuntimeError::new_err(e))
}

/// Register `unity_dlp_js` into `sys.modules` so the Python JCP shim can do
/// `import unity_dlp_js; unity_dlp_js.run_js(stdin)`.
pub fn register_module(py: Python<'_>) -> Result<(), String> {
    (|| -> PyResult<()> {
        let m = PyModule::new_bound(py, "unity_dlp_js")?;
        m.add_function(wrap_pyfunction!(py_run_js, &m)?)?;
        py.import_bound("sys")?.getattr("modules")?.set_item("unity_dlp_js", &m)?;
        Ok(())
    })()
    .map_err(|e| format!("register unity_dlp_js: {e}"))
}
