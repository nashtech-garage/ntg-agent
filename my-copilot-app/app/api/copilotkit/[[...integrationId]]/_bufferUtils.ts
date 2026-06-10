// app/api/copilotkit/[integrationId]/_bufferUtils.ts

export interface StreamChunk {
  content: string;
  contentType: number;
}

/**
 * Extracts distinct JSON chunk items out of a sequential network stream layout buffer.
 */
export function extractObjects(buf: string): { objects: StreamChunk[]; remaining: string } {
  const objects: StreamChunk[] = [];
  
  let sanitized = buf.trim();
  if (sanitized.startsWith("[")) sanitized = sanitized.slice(1);
  if (sanitized.startsWith(",")) sanitized = sanitized.slice(1);

  let i = 0;
  while (i < sanitized.length) {
    const start = sanitized.indexOf("{", i);
    if (start === -1) break;
    
    let depth = 0;
    let end = -1;
    
    for (let j = start; j < sanitized.length; j++) {
      if (sanitized[j] === "{") depth++;
      else if (sanitized[j] === "}") { 
        depth--; 
        if (depth === 0) { 
          end = j; 
          break; 
        } 
      }
    }
    
    if (end === -1) break;
    
    const rawObject = sanitized.slice(start, end + 1);
    try { 
      const parsed = JSON.parse(rawObject);
      const content = parsed.content ?? parsed.Content ?? "";
      const contentType = parsed.contentType ?? parsed.ContentType ?? 0;
      
      objects.push({ content, contentType }); 
    } catch (e) {
      // Catch individual malformed elements without breaking the outer buffer iterator
      console.error("[Stream Parser Error] Failed to parse JSON fragment:", e);
    }
    i = end + 1;
  }
  
  let remaining = sanitized.slice(i).trim();
  if (remaining === "]") remaining = "";
  
  return { objects, remaining };
}