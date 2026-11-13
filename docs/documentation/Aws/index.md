# Amazon AWS connectors

- [Kinesis](Kinesis.md)
- [Simple Notification Service (SNS)](Sns.md)
- [Simple Queue Service (SQS)](Sqs.md)

SQS currently uses AWS SDK v3. SNS and Kinesis use SDK v4. Keep incompatible client dependency graphs separate, including in tests. Emulator coverage does not establish AWS IAM, quotas or managed-service durability.
