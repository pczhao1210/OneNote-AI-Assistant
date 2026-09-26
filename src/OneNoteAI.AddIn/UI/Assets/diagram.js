/* The source is passed as JSON by the host, never interpolated into HTML. */
window.renderDiagram = async function (source) {
    const target = document.getElementById("diagram");
    try {
        mermaid.initialize({
            startOnLoad: false,
            securityLevel: "strict",
            suppressErrorRendering: true,
            maxTextSize: 50000,
            theme: "neutral",
            flowchart: { htmlLabels: false },
            secure: ["secure", "securityLevel", "startOnLoad", "maxTextSize", "suppressErrorRendering"]
        });
        const result = await mermaid.render("local-diagram", source);
        target.innerHTML = result.svg;
        // No bindFunctions: diagram click directives cannot call the host or open links.
        target.querySelectorAll("a").forEach(link => {
            link.removeAttribute("href");
            link.removeAttribute("xlink:href");
        });
        window.chrome.webview.postMessage("rendered");
    } catch {
        const original = document.createElement("pre");
        original.textContent = source;
        target.replaceChildren(original);
        window.chrome.webview.postMessage("error");
    }
};
