import json
import py_compile
import sys


def main():
    payload = json.load(sys.stdin)
    file_path = payload.get("tool_input", {}).get("file_path", "")

    if not file_path.endswith(".py"):
        return

    try:
        py_compile.compile(file_path, doraise=True)
    except py_compile.PyCompileError as e:
        print(json.dumps({
            "decision": "block",
            "reason": f"Syntax error in {file_path}:\n{e}",
        }))


if __name__ == "__main__":
    main()
