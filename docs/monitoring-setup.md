# NTG Agent - Comprehensive Monitoring & Error Tracking Setup

## 🎯 What We've Built

### **Production-Ready Monitoring System**
- **Error Tracking**: Global exception handling with detailed context
- **Performance Metrics**: Custom business metrics and timing data
- **Health Monitoring**: Database, memory, and service health checks
- **Correlation Tracking**: Request tracing across all services
- **Application Insights**: Cloud-based telemetry and analytics
- **Environment-Specific Logging**: Production, development configurations

---

## 🚀 Features Overview

### **1. Enhanced Error Tracking**
- **Critical Error Detection**: Automatic identification of system-threatening issues
- **Detailed Context**: Request headers, user info, stack traces, correlation IDs
- **Security Event Logging**: Authentication failures, unauthorized access attempts
- **Real-time Alerting**: Critical errors trigger immediate notifications

### **2. Performance Monitoring**
- **Custom Metrics**: Business events, user actions, performance counters
- **Automatic Timing**: Request/response duration tracking
- **Resource Monitoring**: Memory usage, database performance
- **Business Intelligence**: Document operations, agent usage statistics

### **3. Health Checks**
- **Basic Health Monitoring**: Standard ASP.NET Core health checks
- **Service Dependencies**: External service availability monitoring
- **Liveness Probes**: Simple application responsiveness checks

### **4. Centralized Logging**
- **Structured Logging**: JSON-formatted logs for easy parsing
- **Correlation IDs**: Track requests across microservices
- **Environment Enrichment**: Machine name, application, environment data
- **File Rotation**: Automatic log file management with retention policies

---

## 🔧 Configuration Examples

### **Application Insights Setup**
```json
{
  "ApplicationInsights": {
    "InstrumentationKey": "your-app-insights-key",
    "ConnectionString": "InstrumentationKey=your-key;IngestionEndpoint=..."
  }
}
```

### **Production Logging Configuration**
```json
{
  "Serilog": {
    "MinimumLevel": "Information",
    "WriteTo": [
      {
        "Name": "Console",
        "Args": { "restrictedToMinimumLevel": "Warning" }
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/ntg-agent-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 30
        }
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/errors/ntg-agent-errors-.log",
          "rollingInterval": "Day",
          "restrictedToMinimumLevel": "Error",
          "retainedFileCountLimit": 90
        }
      }
    ]
  }
}
```

---

## 📊 Monitoring Endpoints

### **Health Check URLs**
- `/health` - Overall application health (development only)
- `/alive` - Basic liveness probe (development only)

### **Sample Health Check Response**
```json
{
  "status": "Healthy",
  "totalDuration": "00:00:00.0123456",
  "entries": {
    "self": {
      "status": "Healthy",
      "tags": ["live"]
    }
  }
}
```

---

## 🔍 Log Sample Output

```
[2025-01-30 10:15:23.456 +00:00] [INF] [abc123-def456] [NTG.Agent.Orchestrator] 
User action performed. UserId: user123, Action: GetDocuments, Data: {"AgentId": "guid-here"}

[2025-01-30 10:15:23.789 +00:00] [INF] [abc123-def456] [NTG.Agent.Orchestrator] 
Business event occurred. Event: DocumentsRetrieved, Data: {"AgentId": "guid-here", "Count": 5}

[2025-01-30 10:15:23.999 +00:00] [ERR] [abc123-def456] [NTG.Agent.Orchestrator] 
CRITICAL ERROR - Immediate attention required. CorrelationId: abc123-def456, Details: {...}
```

---

## 🚨 Production Deployment Checklist

### **Before Deployment**
- [ ] Configure Application Insights connection string
- [ ] Set up log file permissions and directories
- [ ] Set up alerting rules for critical errors
- [ ] Test correlation ID propagation across services
- [ ] Configure external health monitoring if needed

### **Monitoring Setup**
- [ ] Configure log retention policies
- [ ] Set up dashboard for key metrics
- [ ] Configure automated alerts for:
  - Critical error rates
  - Response time degradation
  - Application availability

### **Security Considerations**
- [ ] Mask sensitive data in logs
- [ ] Secure log file access
- [ ] Configure proper user authentication tracking
- [ ] Set up security event monitoring

---

## 🎛️ Key Metrics to Monitor

### **Performance Metrics**
- Request duration (p95, p99)
- Custom business metrics
- Error rates by endpoint
- Response time trends

### **Business Metrics**
- Document upload/download counts
- User activity patterns
- Agent usage statistics
- Knowledge base interactions

### **System Health**
- Service availability
- Application responsiveness
- Error frequency
- Critical error detection

---

## 🔄 Next Steps

1. **Configure Application Insights** for cloud monitoring
2. **Set up alerting rules** for critical errors and performance degradation
3. **Create monitoring dashboards** for key business and technical metrics
4. **Test correlation tracking** across all microservices
5. **Implement log aggregation** for centralized analysis
6. **Add custom health checks** as needed for specific business requirements
