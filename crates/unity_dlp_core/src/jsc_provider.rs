use pyo3::prelude::*;
use rustyscript::{Error as JsError, Runtime, RuntimeOptions};

/// Evaluate `script` inside an isolated V8 context and return the captured
/// console.log output as a string.
///
/// The EJS solver emits its JSON result via a single `console.log(JSON.stringify(…))`
/// call. We prepend a shim that redirects `globalThis.console` to an array so the
/// output can be returned without a subprocess.
pub fn run_js(script: &str) -> Result<String, String> {
    run_js_inner(script).map_err(|e| format!("rustyscript: {e}"))
}

fn run_js_inner(script: &str) -> Result<String, JsError> {
    // Build the wrapped script by string concatenation to avoid Rust's
    // format! treating `{` / `}` inside the JS source as format specifiers.
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
    // '\n' as JS separator is fine; EJS emits exactly one console.log call.
    src.push_str(";return __out.join('\\n');})()");

    let mut rt = Runtime::new(RuntimeOptions::default())?;
    rt.eval::<String>(src)
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
