## Akka.Streams.Xml

This project is a port of akka.stream.alpakka.xml java stream to xml connector.

There are a few difference between this port and the original Java connector:
- Due to .Net XmlReader limitation, Coalesce only supports coalescing CDATA character nodes.
- You should never split text/CDATA character data between two messages or the XmlParser would throw and exception on you. 
If you must fragment a text data, wrap them in CDATA tags before sending.
- This connector supports parsing multiple documents in a single stream.