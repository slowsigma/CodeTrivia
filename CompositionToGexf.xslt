<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet
  version="2.0"
  xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
  xmlns:msxsl="urn:schemas-microsoft-com:xslt"
  
  xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
  
  exclude-result-prefixes="msxsl xsl"
>
  <xsl:output method="xml" indent="yes"/>

  <xsl:template match="Solution">
    <xsl:element name="gexf" namespace="http://www.gexf.net/1.2draft">
      <xsl:attribute name="xsi:schemaLocation">http://www.gexf.net/1.2draft http://www.gexf.net/1.2draft/gexf.xsd</xsl:attribute>

      <xsl:element name="graph">
        <xsl:attribute name="mode">static</xsl:attribute>
        <xsl:attribute name="defaultedgetype">directed</xsl:attribute>

        <attributes class="node">
          <attribute id="0" title="Node Type" type="string"/>
          <attribute id="1" title="Source" type="string"/>
          <attribute id="2" title="Access" type="string"/>
          <attribute id="3" title="Kind" type="string"/>
          <attribute id="4" title="Assembly" type="string"/>
        </attributes>

        <xsl:element name="nodes">
          <xsl:apply-templates select="Project"/>
        </xsl:element>
        <xsl:element name="edges">
          <xsl:apply-templates select="//Reference"/>
        </xsl:element>
      </xsl:element>
    </xsl:element>
  </xsl:template>

  <xsl:template match="Project">
    <xsl:element name="node">
      <xsl:attribute name="id">
        <xsl:value-of select="@FilePath"/>
      </xsl:attribute>
      <xsl:attribute name="label">
        <xsl:value-of select="@Assembly"/>
      </xsl:attribute>

      <attvalues>
        <attvalue for="0" value="Project"/>
        <attvalue for="4">
          <xsl:attribute name="value">
            <xsl:value-of select="@Assembly"/>
          </xsl:attribute>
        </attvalue>
      </attvalues>

      <xsl:apply-templates select="NamedType">
        <xsl:with-param name="Assembly" select="@Assembly"/>
      </xsl:apply-templates>
    </xsl:element>
  </xsl:template>

  <xsl:template match="NamedType">
    <xsl:param name="Assembly"/>
    
    <xsl:element name="node">
      <xsl:attribute name="id">
        <xsl:value-of select="@id"/>
      </xsl:attribute>
      <xsl:attribute name="label">
        <xsl:value-of select="@Name"/>
      </xsl:attribute>

      <attvalues>
        <attvalue for="0" value="Type"/>
        <attvalue for="2">
          <xsl:attribute name="value">
            <xsl:value-of select="@Access"/>
          </xsl:attribute>
        </attvalue>
        <attvalue for="3">
          <xsl:attribute name="value">
            <xsl:value-of select="@Kind"/>
          </xsl:attribute>
        </attvalue>
        <attvalue for="4">
          <xsl:attribute name="value">
            <xsl:value-of select="$Assembly"/>
          </xsl:attribute>
        </attvalue>
      </attvalues>

      <xsl:apply-templates select="NamedType">
        <xsl:with-param name="Assembly" select="$Assembly"/>
      </xsl:apply-templates>
    </xsl:element>
  </xsl:template>

  <xsl:template match="Reference">
    <xsl:element name="edge">
      <xsl:attribute name="id">
        <xsl:value-of select="generate-id()"/>
      </xsl:attribute>
      <xsl:attribute name="source">
        <xsl:value-of select="../@id"/>
      </xsl:attribute>
      <xsl:attribute name="target">
        <xsl:value-of select="@id"/>
      </xsl:attribute>
    </xsl:element>
  </xsl:template>

</xsl:stylesheet>
