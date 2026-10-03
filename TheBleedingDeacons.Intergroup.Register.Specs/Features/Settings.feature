Feature: Where the tablet's settings come from
  As the person who looks after the tablets
  I want settings held on the site to win over what was typed into a tablet
  So that changing a password means changing it once, not on every tablet

  A tablet can take its SMTP, Unity, Better Stack and compliance settings
  from the site's Freedom plugin. Freedom wins only where it holds a value;
  anything it does not hold falls through to the tablet's own setting, so a
  tablet set up by hand keeps working.

  Rule: Freedom wins where it holds a value, and only there

    Scenario Outline: The SMTP settings in use
      Given the tablet's own SMTP <setting> is "<tablet>"
      And Freedom holds "<freedom>" for "<key>"
      Then the SMTP <setting> in use is "<used>"

      Examples:
        | setting | tablet              | key             | freedom           | used              |
        | host    | smtp.tablet.example | smtp.host       | smtp.site.example | smtp.site.example |
        | port    | 587                 | smtp.port       | 465               | 465               |
        | port    | 587                 | smtp.port       | not-a-port        | 587               |
        | TLS     | true                | smtp.enable_ssl | false             | false             |
        | TLS     | true                | smtp.enable_ssl | maybe             | true              |

    Scenario: A setting Freedom does not hold is the tablet's own
      Given the tablet's own SMTP host is "smtp.tablet.example"
      And Freedom holds nothing
      Then the SMTP host in use is "smtp.tablet.example"

    Scenario: A build that names no site leaves Freedom off
      Given a build that names no Freedom site
      Then Freedom is off

  Rule: The Better Stack token goes nowhere it should not

    The source token travels as a bearer credential on every batch of logs.
    An endpoint that is not https would send it in the clear, so shipping
    stops instead (register#47). An endpoint typed without a scheme is
    given one (register#44).

    Scenario Outline: Where logs are shipped
      Given a Better Stack endpoint of "<endpoint>"
      Then logs <verdict> shipped

      Examples:
        | endpoint                 | verdict |
        | https://in.logs.example  | are     |
        | in.logs.example          | are     |
        | http://in.logs.example   | are not |
        |                          | are not |
