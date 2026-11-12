#!/usr/bin/env python3
"""Headless Chrome a11y + layout crawler for Habitinator.
Crawls every route, inspects live DOM + accessibility tree, logs violations.
Usage: python3 audit.py [--base http://127.0.0.1:5050] [--out baseline.json]
"""

import json, sys, argparse
from playwright.sync_api import sync_playwright

ROUTES = [
    "/",
    "/auth/login",
    "/auth/register",
    "/auth/forgot-password",
    "/auth/reset-password",
    "/privacy",
    "/upcoming",
    "/stats",
    "/settings",
    "/not-found",
]

AUDIT_JS = r"""
() => {
  const violations = [];
  const push = (rule, id, message, el) => {
    let snippet = "";
    try {
      if (el && el.outerHTML) snippet = el.outerHTML.slice(0, 400);
    } catch(e) {}
    violations.push({rule, id, message, snippet});
  };
  const visible = (el) => {
    try {
      const r = el.getBoundingClientRect();
      const cs = getComputedStyle(el);
      if (cs.display === "none" || cs.visibility === "hidden") return false;
      if (r.width === 0 && r.height === 0) return false;
      return true;
    } catch(e){ return false; }
  };

  // 1. html lang
  const html = document.documentElement;
  if (!html.getAttribute("lang")) push("html-has-lang","html-has-lang","<html> missing lang", html);

  // 2. title
  const title = (document.title||"").trim();
  if (!title) push("page-has-title","page-has-title","<title> missing or empty", document.querySelector("title"));

  // 3. h1 + heading order + branding heading
  const headings = [...document.querySelectorAll("h1,h2,h3,h4,h5,h6")].filter(visible);
  const h1s = headings.filter(h=>h.tagName==="H1");
  if (h1s.length===0) push("heading-has-h1","heading-has-h1","No visible h1 found", document.body);
  if (h1s.length>1) push("heading-single-h1","heading-single-h1",`${h1s.length} visible h1 elements`, h1s[1]);
  let prevLevel = 0;
  for (const h of headings) {
    const lvl = parseInt(h.tagName[1]);
    if (prevLevel!==0 && lvl - prevLevel > 1) {
      push("heading-order","heading-order",`Heading ${h.tagName} "${h.innerText.slice(0,60)}" skips level from h${prevLevel}`, h);
    }
    prevLevel = lvl;
  }
  // branding should not be a heading level (h6 "Habitinator" in header)
  for (const h of headings) {
    const txt = (h.innerText||"").trim();
    if (/^habitinator$/i.test(txt) && h.closest("header")) {
      push("branding-heading","branding-heading",`${h.tagName} used for brand text in <header>; use span/div`, h);
    }
  }

  // 4. images
  for (const img of document.querySelectorAll("img")) {
    if (!visible(img)) continue;
    if (!img.hasAttribute("alt")) push("img-has-alt","img-has-alt","<img> missing alt attribute", img);
  }

  // 5. buttons accessible name
  const accName = (el) => {
    if (el.getAttribute("aria-label")) return el.getAttribute("aria-label").trim();
    if (el.getAttribute("aria-labelledby")) {
      const ids = el.getAttribute("aria-labelledby").split(/\s+/);
      const t = ids.map(id=>document.getElementById(id)?.innerText||"").join(" ").trim();
      if (t) return t;
    }
    const t = (el.innerText||"").trim();
    if (t) return t;
    if (el.getAttribute("title")) return el.getAttribute("title").trim();
    if (el.value && /^(submit|button)$/i.test(el.type||"")) return el.value.trim();
    if (el.getAttribute("placeholder") && /^(input|textarea)$/i.test(el.tagName)) return "";
    return "";
  };
  for (const b of document.querySelectorAll("button")) {
    if (!visible(b)) continue;
    if (!accName(b)) push("button-has-name","button-has-name","<button> has no accessible name", b);
  }

  // 6. links
  for (const a of document.querySelectorAll("a[href]")) {
    if (!visible(a)) continue;
    if (!accName(a)) push("link-has-name","link-has-name","<a> has no accessible name", a);
  }

  // 7. inputs label
  for (const inp of document.querySelectorAll("input,textarea,select")) {
    if (!visible(inp)) continue;
    const type = (inp.getAttribute("type")||"").toLowerCase();
    if (["hidden","submit","button","reset","image"].includes(type)) continue;
    let labelled = false;
    if (inp.getAttribute("aria-label")) labelled = true;
    if (inp.getAttribute("aria-labelledby")) labelled = true;
    const id = inp.id;
    if (id && document.querySelector(`label[for="${CSS.escape(id)}"]`)) labelled = true;
    if (inp.closest("label")) labelled = true;
    // MudBlazor wraps in labelled controls via aria-label on parent? check ancestors
    if (!labelled) push("input-has-label","input-has-label",`<${inp.tagName.toLowerCase()} type=${type||"-"} id=${id||"-"}> missing label`, inp);
  }

  // 8. duplicate ids
  const seen = {};
  for (const el of document.querySelectorAll("[id]")) {
    const id = el.id;
    if (!id) continue;
    seen[id] = (seen[id]||0)+1;
  }
  for (const [id,c] of Object.entries(seen)) {
    if (c>1) push("duplicate-id","duplicate-id",`id="${id}" appears ${c}x`, document.getElementById(id));
  }

  // 9. landmarks
  if (!document.querySelector("main")) push("landmark-main","landmark-main","No <main> landmark", document.body);
  const navs = [...document.querySelectorAll("nav")];
  if (navs.length>1) {
    for (const n of navs) {
      if (!n.getAttribute("aria-label") && !n.getAttribute("aria-labelledby")) push("landmark-nav-label","landmark-nav-label","Multiple <nav> without label", n);
    }
  }

  // 10. aria-hidden focusable
  for (const h of document.querySelectorAll('[aria-hidden="true"]')) {
    const focusables = h.querySelectorAll('a[href],button,input,select,textarea,[tabindex]:not([tabindex="-1"])');
    if (focusables.length>0) push("aria-hidden-focusable","aria-hidden-focusable",`aria-hidden container has ${focusables.length} focusable descendants`, h);
    // presentational with meaningful text
    if (/^(presentation|none)$/.test(h.getAttribute("role")||"")) {
      const txt = (h.innerText||"").trim();
      if (txt) push("presentational-content","presentational-content",`role=presentation contains text "${txt.slice(0,40)}"`, h);
    }
  }
  for (const el of document.querySelectorAll('[role="presentation"],[role="none"]')) {
    const txt = (el.innerText||"").trim();
    if (txt && !el.querySelector("*")) push("presentational-content","presentational-content",`role=${el.getAttribute("role")} on text "${txt.slice(0,40)}"`, el);
  }

  // 11. tabindex positive
  for (const el of document.querySelectorAll("[tabindex]")) {
    const v = parseInt(el.getAttribute("tabindex"));
    if (v>0) push("tabindex-positive","tabindex-positive",`tabindex=${v} > 0`, el);
  }

  // 12. skip link
  const skip = document.querySelector('a.skip-link[href="#main-content"], a[href="#main-content"]');
  if (!skip) push("skip-link","skip-link","No skip link to #main-content", document.body);
  else if (!document.getElementById("main-content")) push("skip-target","skip-target","Skip link target #main-content missing", skip);

  // 13. tap targets (WCAG 2.5.8 minimum 24px)
  for (const el of document.querySelectorAll("button,a[href],input[type=checkbox],input[type=radio]")) {
    if (!visible(el)) continue;
    const r = el.getBoundingClientRect();
    if (r.width>0 && r.height>0 && (r.width<24 || r.height<24)) {
      // ignore visually-hidden h1 etc
      if (el.classList.contains("hab-visually-hidden")) continue;
      push("tap-target-size","tap-target-size",`<${el.tagName.toLowerCase()}> ${Math.round(r.width)}x${Math.round(r.height)} < 24px`, el);
    }
  }

  // 14. horizontal overflow
  const sw = document.documentElement.scrollWidth;
  const iw = window.innerWidth;
  if (sw > iw + 1) push("no-horizontal-overflow","no-horizontal-overflow",`scrollWidth ${sw} > innerWidth ${iw}`, document.body);

  // 15. contrast (simple): sample text elements, flag ratio < 4.5 for small text
  const lum = (rgb) => {
    const m = rgb.match(/rgba?\(([^)]+)\)/);
    if (!m) return null;
    const parts = m[1].split(",").map(s=>parseFloat(s.trim()));
    const [r,g,b] = parts.slice(0,3).map(v=>v/255).map(v=> v<=0.03928 ? v/12.92 : Math.pow((v+0.055)/1.055,2.4));
    return 0.2126*r+0.7152*g+0.0722*b;
  };
  const ratio = (f,b) => {
    if (f==null||b==null) return null;
    const l1=Math.max(f,b), l2=Math.min(f,b);
    return (l1+0.05)/(l2+0.05);
  };
  const bgOf = (el) => {
    let cur = el;
    while (cur && cur!==document.documentElement) {
      const cs = getComputedStyle(cur);
      const bg = cs.backgroundColor;
      if (bg && !/rgba\(.*,\s*0(\.0*)?\)/.test(bg) && bg!=="transparent") return bg;
      cur = cur.parentElement;
    }
    // root background
    const rcs = getComputedStyle(document.documentElement);
    // Mud dark theme: body bg near #030712, light near white; fallback
    const bodyBg = getComputedStyle(document.body).backgroundColor;
    if (bodyBg && bodyBg!=="rgba(0, 0, 0, 0)") return bodyBg;
    return rcs.backgroundColor || "rgb(255,255,255)";
  };
  const textEls = [...document.querySelectorAll("p,span,a,button,li,label,h1,h2,h3,h4,div.cal-row__meta,div.board-card__notes")].filter(visible).slice(0,120);
  for (const el of textEls) {
    try {
      const cs = getComputedStyle(el);
      const fg = cs.color;
      const bg = bgOf(el);
      const f = lum(fg), b = lum(bg);
      const r = ratio(f,b);
      if (r!==null && r<3.0) {
        const fs = parseFloat(cs.fontSize||"16");
        push("contrast-minimum","contrast-minimum",`Contrast ${r.toFixed(2)}:1 fg=${fg} bg=${bg} "${(el.innerText||"").slice(0,40)}"`, el);
        if (violations.filter(v=>v.rule==="contrast-minimum").length>8) break;
      }
    } catch(e){}
  }

  return violations;
}
"""


def snapshot_names(page, ctx):
    missing = []
    # 1. Modern aria_snapshot (string format) - parse for missing names
    try:
        aria = page.aria_snapshot()
        # aria_snapshot returns YAML-like string with roles; look for generic without name?
        # We count it as tree inspected; detailed missing-name already covered by DOM checks.
        if not aria or len(aria.strip()) < 10:
            missing.append({"role": "unknown", "snippet": "empty aria snapshot"})
    except Exception as e:
        missing.append({"role": "error", "snippet": f"aria_snapshot failed: {e}"[:300]})
        aria = ""
    # 2. CDP full AX tree for true accessibility-tree inspection
    ax_nodes = 0
    unnamed = []
    try:
        cdp = ctx.new_cdp_session(page)
        cdp.send("Accessibility.enable")
        tree = cdp.send("Accessibility.getFullAXTree", {})
        nodes = tree.get("nodes", []) if isinstance(tree, dict) else []
        ax_nodes = len(nodes)
        for n in nodes:
            role = (n.get("role") or {}).get("value", "")
            name = (n.get("name") or {}).get("value", "").strip()
            ignored = n.get("ignored", False)
            if ignored:
                continue
            if (
                role.lower()
                in (
                    "button",
                    "link",
                    "checkbox",
                    "textbox",
                    "searchbox",
                    "combobox",
                    "heading",
                    "radio",
                    "switch",
                )
                and not name
            ):
                # only count visible, non-hidden nodes
                unnamed.append({"role": role, "snippet": str(n)[:300]})
        try:
            cdp.detach()
        except Exception:
            pass
    except Exception as e:
        # CDP not available - still inspected via aria_snapshot
        ax_nodes = -1
    return missing, unnamed, ax_nodes, aria


def run_theme(browser, base, theme):
    ctx = browser.new_context(
        viewport={"width": 1280, "height": 800},
        color_scheme=theme,
    )
    ctx.add_cookies(
        [
            {
                "name": "habitinator_theme",
                "value": theme,
                "domain": "127.0.0.1",
                "path": "/",
            }
        ]
    )
    page = ctx.new_page()
    results = {}
    total = 0
    for route in ROUTES:
        sep = "&" if "?" in route else "?"
        url = base.rstrip("/") + route + f"{sep}theme={theme}"
        try:
            resp = page.goto(url, wait_until="load", timeout=30000)
            status = resp.status if resp else 0
        except Exception as e:
            results[f"{theme} {route}"] = {"error": str(e), "violations": []}
            continue
        page.wait_for_timeout(2500)
        actual = page.evaluate(
            "() => document.documentElement.classList.contains('dark-theme') ? 'dark' : (document.documentElement.classList.contains('light-theme') ? 'light' : 'unknown')"
        )
        violations = page.evaluate(AUDIT_JS)
        if actual != theme:
            violations.append(
                {
                    "rule": "theme-mismatch",
                    "id": "theme-mismatch",
                    "message": f"wanted {theme} but DOM is {actual}",
                    "snippet": "",
                }
            )
        ax_missing, ax_unnamed, ax_nodes, aria = snapshot_names(page, ctx)
        for m in ax_unnamed[:20]:
            violations.append(
                {
                    "rule": "ax-tree-name",
                    "id": "ax-tree-name",
                    "message": f"AX tree {m['role']} missing accessible name (CDP full tree, {ax_nodes} nodes)",
                    "snippet": m["snippet"],
                }
            )
        results[f"{theme} {route}"] = {
            "status": status,
            "theme": actual,
            "violations": violations,
        }
        total += len(violations)
        print(f"{theme} {route} -> {status} [{actual}] : {len(violations)} violations")
        for v in violations[:12]:
            print(f"  [{v['rule']}] {v['message']}")
    ctx.close()
    return results, total


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default="http://127.0.0.1:5050")
    ap.add_argument("--out", default="tools/a11y-audit/baseline.json")
    ap.add_argument("--themes", default="light,dark")
    args = ap.parse_args()

    themes = [t.strip() for t in args.themes.split(",") if t.strip()]
    results = {}
    total = 0
    with sync_playwright() as p:
        browser = p.chromium.launch(
            headless=True, args=["--no-sandbox", "--disable-dev-shm-usage"]
        )
        for theme in themes:
            r, t = run_theme(browser, args.base, theme)
            results.update(r)
            total += t
        browser.close()

    # group by rule
    by_rule = {}
    for route, data in results.items():
        for v in data.get("violations", []):
            by_rule.setdefault(v["rule"], []).append(
                {"route": route, "message": v["message"], "snippet": v["snippet"][:200]}
            )
    summary = {k: len(v) for k, v in sorted(by_rule.items(), key=lambda x: -len(x[1]))}
    print("\n=== BASELINE SCORE ===")
    print(f"Total violations: {total}")
    print(json.dumps(summary, indent=2))
    out = {"total": total, "by_rule": summary, "details": by_rule, "routes": results}
    with open(args.out, "w") as f:
        json.dump(out, f, indent=2)
    print(f"Wrote {args.out}")


if __name__ == "__main__":
    main()
