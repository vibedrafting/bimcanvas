/**
 * 生成 8 位短唯一 ID（小写字母数字）
 * 36^8 ≈ 2.8 万亿种组合，足以满足模块标识需求
 */
export function generateUid(): string {
  const chars = '0123456789abcdefghijklmnopqrstuvwxyz';
  const bytes = new Uint8Array(8);
  crypto.getRandomValues(bytes);
  return Array.from(bytes, b => chars[b % chars.length]).join('');
}
