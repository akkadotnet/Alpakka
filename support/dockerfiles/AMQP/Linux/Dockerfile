FROM rabbitmq:3.8.6-alpine

ADD rabbitmq.conf    /etc/rabbitmq/
ADD enabled_plugins  /etc/rabbitmq/

RUN chmod 644 /etc/rabbitmq/rabbitmq.conf
RUN chmod 644 /etc/rabbitmq/enabled_plugins

ENV RABBITMQ_CONFIG_FILE=$RABBITMQ_HOME/etc/rabbitmq/rabbitmq.conf

EXPOSE 4369 5672 5671 15672
