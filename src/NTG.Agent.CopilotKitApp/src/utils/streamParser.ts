// src/utils/streamParser.ts
export interface PromptChunk {
  content: string;
  contentType: number;
}

export function extractObjects(buf: string): { objects: PromptChunk[]; remaining: string } {
  const objects: PromptChunk[] = [];
  let i = 0;
  while (i < buf.length) {
    const start = buf.indexOf("{", i);
    if (start === -1) break;
    let depth = 0, end = -1;
    for (let j = start; j < buf.length; j++) {
      if (buf[j] === "{") depth++;
      else if (buf[j] === "}") {
        depth--;
        if (depth === 0) {
          end = j;
          break;
        }
      }
    }
    if (end === -1) break;
    try {
      objects.push(JSON.parse(buf.slice(start, end + 1)));
    } catch {}
    i = end + 1;
  }
  return { objects, remaining: buf.slice(i) };
}