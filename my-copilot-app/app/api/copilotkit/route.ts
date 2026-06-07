import {
  CopilotRuntime,
  copilotRuntimeNextJSAppRouterEndpoint,
} from "@copilotkit/runtime";
import { BuiltInAgent } from "@copilotkit/runtime/v2";
import { createAzure } from "@ai-sdk/azure";
import { NextRequest } from "next/server";

// 1. Lấy thông tin cấu hình từ file .env
const apiKey = process.env.AZURE_OPENAI_API_KEY;
const endpoint = process.env.AZURE_OPENAI_ENDPOINT; 
const deployment = process.env.AZURE_OPENAI_DEPLOYMENT_NAME; // gpt-5.4

if (!apiKey || !endpoint || !deployment) {
  throw new Error("Missing Azure OpenAI configuration in environment variables.");
}

// 2. Trích xuất Resource Name từ Endpoint URL
const resourceName = endpoint
  .replace("https://", "")
  .split(".")[0];

// 3. Khởi tạo Azure Provider sử dụng Vercel AI SDK
const azureProvider = createAzure({
  resourceName: resourceName,
  apiKey: apiKey,
});

// 4. Khởi tạo Agent sử dụng Model Deployment (gpt-5.4)
const agent = new BuiltInAgent({
  model: azureProvider.chat(deployment), 
});

// 5. Đưa agent vào Runtime của CopilotKit
const runtime = new CopilotRuntime({
  agents: { default: agent },
});

// 6. Định nghĩa Route Handler sử dụng helper của v2
export const POST = async (req: NextRequest) => {
  const { handleRequest } = copilotRuntimeNextJSAppRouterEndpoint({
    runtime,
    endpoint: "/api/copilotkit",
  });

  return handleRequest(req);
};

// Đăng ký thêm hàm GET để tránh lỗi 404 khi Client thực hiện ping kiểm tra thông tin Runtime
export const GET = async (req: NextRequest) => {
  const { handleRequest } = copilotRuntimeNextJSAppRouterEndpoint({
    runtime,
    endpoint: "/api/copilotkit",
  });

  return handleRequest(req);
};