Feature: Where the tablet's settings come from
  As the person who looks after the tablets
  I want every credential and endpoint held on the site and nowhere else
  So that changing a password means changing it once, and no tablet is ever quietly pointed somewhere else

  A tablet takes its SMTP, Unity, Better Stack and compliance settings from
  the site's Freedom plugin, and from nowhere else: nothing is built into the
  app, and nothing can be typed in on the tablet. The build names only which
  Freedom site to ask. A key Freedom does not hold is not set at all, and the
  page that needs it says so. Only the shape of a connection has a default:
  port 587, TLS on.

  Rule: Credentials and endpoints come from Freedom and nowhere else

    Scenario Outline: The SMTP settings in use
      Given Freedom holds "<freedom>" for "<key>"
      Then the SMTP <setting> in use is "<used>"

      Examples:
        | setting | key             | freedom           | used              |
        | host    | smtp.host       | smtp.site.example | smtp.site.example |
        | port    | smtp.port       | 465               | 465               |
        | port    | smtp.port       | not-a-port        | 587               |
        | TLS     | smtp.enable_ssl | false             | false             |
        | TLS     | smtp.enable_ssl | maybe             | true              |

    Scenario: The Unity site and the compliance contact are Freedom's
      Given Freedom holds "https://aa-bristol.org/amber" for "unity.base_url"
      And Freedom holds "compliance@aa-bristol.org" for "compliance.email"
      Then the Unity site in use is "https://aa-bristol.org/amber"
      And the compliance contact in use is "compliance@aa-bristol.org"

    Scenario: A tablet Freedom tells nothing has no settings at all
      Given Freedom holds nothing
      Then the SMTP host in use is ""
      And SMTP is not set up
      And the Unity site in use is ""
      And Better Stack is not set up
      And the compliance contact in use is ""

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
