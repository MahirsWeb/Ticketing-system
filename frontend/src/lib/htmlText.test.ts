import { describe, expect, it } from 'vitest';
import { isBlankHtml } from './htmlText';

describe('isBlankHtml', () => {
  it.each([null, undefined, '', '   ', '<p></p>', '<p> </p>', '<p>&nbsp;</p>', '<p>&nbsp; &#160;</p><p><br></p>', '<p>\u200b</p>', '<div><span></span></div>'])(
    'says %j is blank',
    (html) => expect(isBlankHtml(html)).toBe(true)
  );

  it.each(['<p>x</p>', '<p>&lt;</p>', '<p>  Pozdrav  </p>', '<ul><li>stavka</li></ul>', '<p><img src="data:image/png;base64,AAAA"></p>', '<IMG SRC="data:image/png;base64,AAAA">'])(
    'says %j is not blank',
    (html) => expect(isBlankHtml(html)).toBe(false)
  );
});
