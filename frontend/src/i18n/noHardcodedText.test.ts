import { describe, expect, it } from 'vitest';
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, relative, resolve } from 'node:path';
import ts from 'typescript';

// The parity test (translations.test.tsx) shows that every key has an English and a Bosnian text. It cannot show that a screen uses the keys: a
// sentence typed straight into a component is invisible to it, and stays English when the language is switched. This reads every component
// the way a compiler does and fails on text that is not going through t().

const SRC = resolve(__dirname, '..');

/** Text that is deliberately the same in every language, or that is not text at all. Each has a reason. */
const ALLOWED = new Set([
  'EN', // the language switcher names each language by its own code
  'BS',
  '+387 6X XXX XXX', // an example of the format of a telephone number
  'servis@firma.ba', // examples of the format of an e-mail address
  'client@company.com',
]);

/** Attributes whose words a person reads or hears. */
const TEXT_ATTRIBUTES = new Set(['placeholder', 'title', 'aria-label', 'ariaLabel', 'alt', 'label', 'confirmLabel', 'emptyText', 'description', 'message', 'heading', 'subtitle', 'tooltip', 'name']);

function files(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    if (statSync(path).isDirectory()) return ['test', 'build'].includes(name) ? [] : files(path);
    return /\.tsx?$/.test(name) && !/\.test\.tsx?$/.test(name) && !/\.d\.ts$|generated|contract\.check/.test(name) ? [path] : [];
  });
}

const isWords = (text: string) => /[A-Za-z]{2,}/.test(text) && !/^[\w\-./:?=&%#${}[\]()@+,]+$/.test(text.trim());

function untranslated(path: string): string[] {
  const text = readFileSync(path, 'utf8');
  const source = ts.createSourceFile(path, text, ts.ScriptTarget.Latest, true, path.endsWith('.tsx') ? ts.ScriptKind.TSX : ts.ScriptKind.TS);
  const found: string[] = [];
  const at = (node: ts.Node, what: string) => found.push(`${relative(SRC, path).replace(/\\/g, '/')}:${source.getLineAndCharacterOfPosition(node.getStart()).line + 1} ${what}`);

  const insideJsx = (node: ts.Node) => {
    for (let n = node.parent; n; n = n.parent) if (ts.isJsxElement(n) || ts.isJsxSelfClosingElement(n) || ts.isJsxFragment(n)) return true;
    return false;
  };
  const insideTranslationCall = (node: ts.Node) => {
    for (let n = node.parent; n; n = n.parent) {
      // t('key') names a text; format(date, 'dd.MM.yyyy') names a pattern: neither is text
      if (ts.isCallExpression(n) && /^(t|tr|format)$/.test(n.expression.getText())) return true;
      if (ts.isJsxAttribute(n)) return !TEXT_ATTRIBUTES.has(n.name.getText());
      if (ts.isImportDeclaration(n) || ts.isTypeNode(n) || ts.isCaseClause(n)) return true;
      if (ts.isBinaryExpression(n) && /^(===|!==|==|!=)$/.test(n.operatorToken.getText())) return true;
      if (ts.isPropertyAssignment(n) && n.name === node) return true;
      if (ts.isElementAccessExpression(n)) return true;
    }
    return false;
  };

  const calls = /^(setError|setMessage|setNotice|setSendError|setStatus|setAvatarError|alert)$/;
  const textual = /^(label|title|message|text|placeholder|description|heading|hint|tooltip|emptyText|confirmLabel)$/;

  const visit = (node: ts.Node) => {
    if (ts.isCallExpression(node)) {
      const callee = node.expression.getText();
      // A sentence handed straight to what shows an error or a notice, or as the fallback words of messageOf(err, 'fallback').
      const given = calls.test(callee) ? node.arguments : callee === 'messageOf' ? node.arguments.slice(1) : [];
      for (const argument of given)
        if ((ts.isStringLiteral(argument) || ts.isNoSubstitutionTemplateLiteral(argument)) && isWords(argument.text) && !ALLOWED.has(argument.text))
          at(argument, `shown "${argument.text}"`);
    } else if (ts.isPropertyAssignment(node) && textual.test(node.name.getText()) && (ts.isStringLiteral(node.initializer) || ts.isNoSubstitutionTemplateLiteral(node.initializer))) {
      const words = node.initializer.text;
      if (isWords(words) && !ALLOWED.has(words)) at(node.initializer, `${node.name.getText()}: "${words}"`);
    }
    if (ts.isJsxText(node)) {
      const words = node.getText().replace(/\s+/g, ' ').trim();
      if (words && /[A-Za-z]{2,}/.test(words) && !ALLOWED.has(words)) at(node, `text "${words}"`);
    } else if ((ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) && insideJsx(node) && !insideTranslationCall(node)) {
      const words = node.text;
      if (isWords(words) && !ALLOWED.has(words)) at(node, `literal "${words}"`);
    } else if (ts.isTemplateExpression(node) && insideJsx(node) && !insideTranslationCall(node)) {
      const words = [node.head.text, ...node.templateSpans.map((s) => s.literal.text)].join(' ').trim();
      if (isWords(words) && !ALLOWED.has(words)) at(node, `template "${words}"`);
    }
    ts.forEachChild(node, visit);
  };
  visit(source);
  return found;
}

describe('the screens', () => {
  it('have no sentence, label or hint typed into them that would stay English after the language is switched', () => {
    const offenders = files(SRC).flatMap(untranslated);
    expect(offenders).toEqual([]);
  });

  it('are really read (the check above is not passing because it looked at nothing)', () => {
    expect(files(SRC).length).toBeGreaterThan(150);
  });
});
